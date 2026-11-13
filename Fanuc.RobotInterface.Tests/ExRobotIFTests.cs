using System.Net;
using System.Net.Sockets;
using Fanuc.RobotInterface;
using Fanuc.SRTP;

namespace Fanuc.RobotInterface.Tests;

[TestClass]
public sealed class ExRobotIFTests
{
    [TestMethod]
#pragma warning disable MSTEST0045 // Bound the test even if synchronous socket code stops responding.
    [Timeout(10000)]
#pragma warning restore MSTEST0045
    public async Task ConnectAndReadThroughRobotIF()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        using var releaseServer = new ManualResetEventSlim();
        var requests = new List<byte[]>();
        var server = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            client.ReceiveTimeout = 3000;
            using var stream = client.GetStream();
            for (var request = 0; request < 4; request++)
            {
                var header = new byte[PacketBase.HEADER_LENGTH];
                var offset = 0;
                while (offset < header.Length)
                {
                    var received = stream.Read(header, offset, header.Length - offset);
                    if (received == 0)
                        throw new IOException("Client closed before sending the request.");
                    offset += received;
                }
                requests.Add(header);

                var response = new byte[PacketBase.HEADER_LENGTH];
                response[31] = (byte)MessageType.SHORT_ACK;
                if (request == 3)
                    response[44] = 1;
                stream.Write(response, 0, response.Length);
            }
            if (!releaseServer.Wait(3000))
                throw new TimeoutException("Client did not finish.");
        });

        var robot = new ExRobotIF(new RobotIF());
        try
        {
            await robot.ConnectAsync("127.0.0.1", port, 3000);
            Assert.IsTrue(robot.IsConnected);
            Assert.AreEqual("127.0.0.1", robot.Host);
            var input = await robot.AsyncReadDI(1, 1);
            CollectionAssert.AreEqual(new[] { true }, input);
        }
        finally
        {
            try
            {
                robot.Disconnect();
            }
            finally
            {
                releaseServer.Set();
                listener.Stop();
            }
        }

        await server;
        Assert.AreEqual((byte)PacketType.INIT, requests[0][0]);
        Assert.AreEqual((byte)PacketType.UNKNOWN_8, requests[1][0]);
        Assert.AreEqual((byte)ServiceRequestCode.WRITE_SYS_MEM, requests[2][42]);
        CollectionAssert.AreEqual(System.Text.Encoding.ASCII.GetBytes("CLRASG"),
            requests[2].Skip(48).Take(6).ToArray());
        Assert.AreEqual((byte)ServiceRequestCode.READ_SYS_MEM, requests[3][42]);
        Assert.AreEqual((byte)SegmentSelector.BIT_Q, requests[3][43]);
    }
}
