using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Fanuc.RobotInterface;
using Fanuc.SRTP;

namespace Fanuc.RobotInterface.Tests;

[TestClass]
public sealed class RobotIFTransportTests
{
    private const string EofChildVariable = "FANUC_TRANSPORT_EOF_TEST_CHILD";

    [TestMethod]
    public void FragmentedHeaderAndPayloadAreReceivedCompletely()
    {
        var expected = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var server = new TestServer(socket =>
        {
            ReadRequest(socket);
            var header = ResponseHeader();
            SendAll(socket, header, 0, 7);
            Thread.Sleep(200);
            SendAll(socket, header, 7, header.Length - 7);

            ReadRequest(socket);
            SendAll(socket, ResponseHeader());
            ReadRequest(socket);
            SendAll(socket, ResponseHeader(expected.Length));
            SendAll(socket, expected, 0, 3);
            Thread.Sleep(200);
            SendAll(socket, expected, 3, expected.Length - 3);
        });

        var robot = new RobotIF();
        try
        {
            robot.Connect("127.0.0.1", server.Port, 3000);
            CollectionAssert.AreEqual(expected, robot.ReadSNPX(1, (ushort)expected.Length));
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(13)]
    public void TruncatedHeaderFailsWithEndOfStream(int bytesToSend)
    {
        using var server = new TestServer(socket =>
        {
            ReadRequest(socket);
            SendAll(socket, ResponseHeader(), 0, bytesToSend);
            socket.Shutdown(SocketShutdown.Send);
        });

        var robot = new RobotIF();
        try
        {
            var error = Assert.ThrowsExactly<IOException>(
                () => robot.Connect("127.0.0.1", server.Port, 3000));
            Assert.IsInstanceOfType<EndOfStreamException>(error.InnerException);
            StringAssert.Contains(error.InnerException.Message, $"{bytesToSend} of 56");
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void TruncatedPayloadRaisesEndOfStream()
        => RunIsolatedEofTest(nameof(TruncatedPayloadRaisesEndOfStream), false);

    [TestMethod]
    public void TruncatedPayloadDuringReadRaisesEndOfStream()
        => RunIsolatedEofTest(nameof(TruncatedPayloadDuringReadRaisesEndOfStream), true);

    private static void RunIsolatedEofTest(string method, bool duringRead)
    {
        if (Environment.GetEnvironmentVariable(EofChildVariable) == method)
        {
            VerifyTruncatedPayload(duringRead);
            return;
        }

        // Isolate the regression: the old receive loop spins forever after EOF.
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
#if NETFRAMEWORK
        var start = new ProcessStartInfo(assemblyPath);
#else
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            Arguments = $"\"{assemblyPath}\" "
        };
#endif
        start.Arguments += $"--filter \"FullyQualifiedName=Fanuc.RobotInterface.Tests.RobotIFTransportTests.{method}\" --minimum-expected-tests 1";
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        start.EnvironmentVariables[EofChildVariable] = method;
        using var child = Process.Start(start);
        Assert.IsNotNull(child);
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        var exited = child.WaitForExit(10000);
        if (!exited)
        {
            child.Kill();
            child.WaitForExit();
        }

        var details = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
        Assert.IsTrue(exited, $"The isolated EOF test did not finish: {details}");
        Assert.AreEqual(0, child.ExitCode, details);
    }

    [TestMethod]
    public void LargeExtendedRequestReachesSlowPeerIntact()
    {
        var expected = Enumerable.Range(0, 60000).Select(i => (byte)(i % 251)).ToArray();
        byte[]? received = null;
        using var server = new TestServer(socket =>
        {
            ReadRequest(socket);
            SendAll(socket, ResponseHeader());
            ReadRequest(socket);
            SendAll(socket, ResponseHeader());
            Thread.Sleep(200);
            received = ReadRequest(socket);
            SendAll(socket, ResponseHeader());
        });

        var robot = new RobotIF();
        try
        {
            robot.Connect("127.0.0.1", server.Port, 3000);
            robot.WriteSNPX(1, expected);
            server.Complete();
            Assert.IsNotNull(received);
            Assert.AreEqual(expected.Length, PacketBase.ExtraLengthFromHeaderBuffer(received));
            CollectionAssert.AreEqual(expected, received.Skip(PacketBase.HEADER_LENGTH).ToArray());
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void RequestWithoutConnectionFailsClearly()
    {
        var robot = new RobotIF();
        var error = Assert.ThrowsExactly<IOException>(() => robot.ReadDI(1, 1));
        StringAssert.Contains(error.Message, "not connected");
    }

    private static void VerifyTruncatedPayload(bool duringRead)
    {
        using var server = new TestServer(socket =>
        {
            if (duringRead)
            {
                ReadRequest(socket);
                SendAll(socket, ResponseHeader());
                ReadRequest(socket);
                SendAll(socket, ResponseHeader());
            }
            ReadRequest(socket);
            SendAll(socket, ResponseHeader(8));
            SendAll(socket, new byte[] { 1, 2, 3 });
            socket.Shutdown(SocketShutdown.Send);
        });

        var robot = new RobotIF();
        try
        {
            if (duringRead)
            {
                robot.Connect("127.0.0.1", server.Port, 3000);
                var error = Assert.ThrowsExactly<EndOfStreamException>(() => robot.ReadSNPX(1, 8));
                StringAssert.Contains(error.Message, "3 of 8");
            }
            else
            {
                var error = Assert.ThrowsExactly<IOException>(
                    () => robot.Connect("127.0.0.1", server.Port, 3000));
                Assert.IsInstanceOfType<EndOfStreamException>(error.InnerException);
                StringAssert.Contains(error.InnerException.Message, "3 of 8");
            }
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    private static byte[] ResponseHeader(int extraLength = 0)
    {
        var header = new byte[PacketBase.HEADER_LENGTH];
        header[4] = (byte)extraLength;
        header[5] = (byte)(extraLength >> 8);
        header[31] = (byte)MessageType.SHORT_ACK;
        return header;
    }

    private static byte[] ReadRequest(Socket socket)
    {
        var header = ReadExactly(socket, PacketBase.HEADER_LENGTH);
        var payload = ReadExactly(socket, PacketBase.ExtraLengthFromHeaderBuffer(header));
        return header.Concat(payload).ToArray();
    }

    private static byte[] ReadExactly(Socket socket, int count)
    {
        var result = new byte[count];
        var offset = 0;
        while (offset < result.Length)
        {
            var received = socket.Receive(result, offset, result.Length - offset, SocketFlags.None);
            if (received == 0)
                throw new EndOfStreamException("Client closed before completing the request.");
            offset += received;
        }
        return result;
    }

    private static void SendAll(Socket socket, byte[] bytes) => SendAll(socket, bytes, 0, bytes.Length);

    private static void SendAll(Socket socket, byte[] bytes, int offset, int count)
    {
        var end = offset + count;
        while (offset < end)
        {
            var sent = socket.Send(bytes, offset, end - offset, SocketFlags.None);
            if (sent == 0)
                throw new EndOfStreamException("Client closed before receiving the response.");
            offset += sent;
        }
    }

    private sealed class TestServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly Task _task;

        public ushort Port { get; }

        public TestServer(Action<Socket> handler)
        {
            _listener.Start();
            Port = (ushort)((IPEndPoint)_listener.LocalEndpoint).Port;
            _task = Task.Run(() =>
            {
                using var socket = _listener.AcceptSocket();
                socket.NoDelay = true;
                socket.ReceiveTimeout = 3000;
                socket.SendTimeout = 3000;
                handler(socket);
            });
        }

        public void Complete() => _task.GetAwaiter().GetResult();

        public void Dispose() => _listener.Stop();
    }
}
