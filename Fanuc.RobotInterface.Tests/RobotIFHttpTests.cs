using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Fanuc.RobotInterface;
using Fanuc.SRTP;

namespace Fanuc.RobotInterface.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RobotIFHttpTests
{
    [TestMethod]
    public void ConstructorValidatesAndExposesEndpoints()
    {
        var robot = new RobotIF("localhost");
        Assert.AreEqual("localhost", robot.Host);
        Assert.AreEqual((ushort)60008, robot.SrtpPort);
        Assert.AreEqual((ushort)80, robot.HttpPort);
        var custom = new RobotIF("localhost", 1, ushort.MaxValue);
        Assert.AreEqual((ushort)1, custom.SrtpPort);
        Assert.AreEqual(ushort.MaxValue, custom.HttpPort);
        Assert.ThrowsExactly<ArgumentNullException>(() => new RobotIF(null!));
        Assert.ThrowsExactly<ArgumentException>(() => new RobotIF(""));
        Assert.ThrowsExactly<ArgumentException>(() => new RobotIF(" "));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RobotIF("localhost", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RobotIF("localhost", httpPort: 0));
    }

    [TestMethod]
    public void HttpUriPreservesScopedIpv6AndQuery()
    {
        var robot = new RobotIF("fe80::1%12", httpPort: 8080);
        var method = typeof(RobotIF).GetMethod("HttpUri", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        var uri = method.Invoke(robot, new object[] { "KAREL/demo?arg=1" }) as Uri;
        Assert.IsNotNull(uri);
        Assert.AreEqual("fe80::1%12", uri.DnsSafeHost);
        Assert.AreEqual(8080, uri.Port);
        Assert.AreEqual("/KAREL/demo", uri.AbsolutePath);
        Assert.AreEqual("?arg=1", uri.Query);
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task HttpWorksWithoutAndAfterFailedSrtpConnection()
    {
        using var http = new HttpServer(
            ("/KCL/status", "<BODY><XMP>OK</XMP></BODY>"),
            ("/KAREL/demo?arg=1", "KAREL result"),
            ("/KCL/reset", "<BODY><XMP>Reset</XMP></BODY>"),
            ("/KCL/reset", "<BODY><XMP>Reset</XMP></BODY>"),
            ("/KAREL/after", "After disconnect"));
        var srtp = new TcpListener(IPAddress.Loopback, 0);
        srtp.Start();
        try
        {
            var srtpPort = (ushort)((IPEndPoint)srtp.LocalEndpoint).Port;
            var accepting = Task.Run(() =>
            {
                for (var i = 0; i < 2; i++)
                {
                    using var socket = srtp.AcceptSocket();
                }
            });
            var robot = new RobotIF("127.0.0.1", srtpPort, http.Port);
            var wrapper = new ExRobotIF(robot);
            IRobotIF api = wrapper;
            Assert.AreEqual(robot.Host, api.Host);
            Assert.AreEqual(srtpPort, api.SrtpPort);
            Assert.AreEqual(http.Port, api.HttpPort);
            Assert.AreEqual("OK", await Task.Run(() => robot.ExecuteKCL("status")));
            await Assert.ThrowsExactlyAsync<IOException>(() => Task.Run(() => robot.Connect(1000)));
            await Assert.ThrowsExactlyAsync<IOException>(() => wrapper.ConnectAsync(1000));
            await accepting;
            Assert.IsFalse(wrapper.IsConnected);
            Assert.AreEqual("KAREL result", await wrapper.AsyncExecuteKarel("demo?arg=1"));
            robot.Disconnect();
            Assert.AreEqual("Reset", await Task.Run(() => api.ExecuteKCL("reset")));
            await Task.Run(() => api.ResetAlarm());
            Assert.AreEqual("After disconnect", await wrapper.AsyncExecuteKarel("after"));
            await http.Complete();
        }
        finally
        {
            srtp.Stop();
        }
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task RobotsUseTheirOwnHttpEndpoints()
    {
        using var first = new HttpServer(("/KAREL/first", "first"));
        using var second = new HttpServer(("/KAREL/second", "second"));
        first.CookieToSet = "robot=one; Path=/";
        var firstRobot = new RobotIF("127.0.0.1", httpPort: first.Port);
        var secondRobot = new RobotIF("127.0.0.1", httpPort: second.Port);
        Assert.AreEqual("first", await Task.Run(() => firstRobot.ExecuteKarel("first")));
        Assert.AreEqual("second", await Task.Run(() => secondRobot.ExecuteKarel("second")));
        await Task.WhenAll(first.Complete(), second.Complete());
        Assert.IsFalse(second.Headers.Any(line => line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)),
            "The other robot's HTTP cookie leaked into this request.");
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task KclErrorsRemainVisible()
    {
        using var http = new HttpServer(("/KCL/fail", "<BODY>ERROR: failure</BODY>"));
        var robot = new RobotIF("127.0.0.1", httpPort: http.Port);
        var error = await Assert.ThrowsExactlyAsync<IOException>(() => Task.Run(() => robot.ExecuteKCL("fail")));
        StringAssert.Contains(error.Message, "failure");
        await http.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(30000)]
#pragma warning restore MSTEST0045
    public async Task SrtpDisconnectDoesNotWaitForAnHttpRequest()
    {
        var http = new TcpListener(IPAddress.Loopback, 0);
        var srtp = new TcpListener(IPAddress.Loopback, 0);
        using var releaseHttp = new ManualResetEventSlim();
        var httpReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.Start();
        srtp.Start();
        var httpTask = Task.Run(() =>
        {
            using var socket = http.AcceptSocket();
            socket.ReceiveTimeout = 10000;
            using var stream = new NetworkStream(socket);
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            Assert.AreEqual("GET /KAREL/slow HTTP/1.1", reader.ReadLine());
            while (!string.IsNullOrEmpty(reader.ReadLine())) { }
            httpReceived.TrySetResult(true);
            Assert.IsTrue(releaseHttp.Wait(10000), "HTTP response was not released.");
            var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
            stream.Write(response, 0, response.Length);
        });
        var srtpTask = Task.Run(() =>
        {
            using var socket = srtp.AcceptSocket();
            socket.ReceiveTimeout = 10000;
            using var stream = new NetworkStream(socket);
            for (var i = 0; i < 3; i++)
            {
                var header = ReadExactly(stream, PacketBase.HEADER_LENGTH);
                ReadExactly(stream, PacketBase.ExtraLengthFromHeaderBuffer(header));
                var response = new byte[PacketBase.HEADER_LENGTH];
                response[31] = (byte)MessageType.SHORT_ACK;
                stream.Write(response, 0, response.Length);
            }
            Assert.AreEqual(-1, stream.ReadByte());
        });
        var robot = new ExRobotIF(new RobotIF("127.0.0.1",
            (ushort)((IPEndPoint)srtp.LocalEndpoint).Port,
            (ushort)((IPEndPoint)http.LocalEndpoint).Port));
        try
        {
            await Bounded(robot.ConnectAsync(5000));
            var request = robot.AsyncExecuteKarel("slow");
            await Bounded(httpReceived.Task);
            await Bounded(Task.Run(robot.Disconnect));
            Assert.IsFalse(request.IsCompleted, "SRTP disconnect completed the HTTP request.");
            releaseHttp.Set();
            Assert.AreEqual("OK", await Bounded(request));
            await Bounded(Task.WhenAll(httpTask, srtpTask));
        }
        finally
        {
            releaseHttp.Set();
            robot.Disconnect();
            http.Stop();
            srtp.Stop();
        }
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var bytes = new byte[count];
        for (var offset = 0; offset < count;)
        {
            var read = stream.Read(bytes, offset, count - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }

    private static async Task Bounded(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(5000)) != task)
            throw new TimeoutException("Operation did not finish in time.");
        await task;
    }

    private static async Task<T> Bounded<T>(Task<T> task)
    {
        await Bounded((Task)task);
        return await task;
    }

    private sealed class HttpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _task;
        public ushort Port { get; }
        public string? CookieToSet { get; set; }
        public List<string> Headers { get; } = new();

        public HttpServer(params (string Path, string Body)[] responses)
        {
            _listener.Start();
            Port = (ushort)((IPEndPoint)_listener.LocalEndpoint).Port;
            _task = Task.Run(() =>
            {
                foreach (var response in responses)
                {
                    using var socket = _listener.AcceptSocket();
                    socket.ReceiveTimeout = 5000;
                    socket.SendTimeout = 5000;
                    using var stream = new NetworkStream(socket, ownsSocket: false);
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var request = reader.ReadLine();
                    Assert.AreEqual($"GET {response.Path} HTTP/1.1", request);
                    string? line;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                        Headers.Add(line);
                    var body = Encoding.UTF8.GetBytes(response.Body);
                    var cookie = CookieToSet == null ? "" : $"Set-Cookie: {CookieToSet}\r\n";
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n{cookie}Connection: close\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    stream.Write(body, 0, body.Length);
                }
            });
        }

        public async Task Complete()
        {
            if (await Task.WhenAny(_task, Task.Delay(5000)) != _task)
                throw new TimeoutException("HTTP server did not finish in time.");
            await _task;
        }

        public void Dispose() => _listener.Stop();
    }
}
