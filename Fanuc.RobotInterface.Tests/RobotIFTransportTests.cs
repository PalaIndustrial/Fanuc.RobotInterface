using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Fanuc.RobotInterface;
using Fanuc.SRTP;

namespace Fanuc.RobotInterface.Tests;

[TestClass]
[DoNotParallelize]
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TimedOutWriteClosesConnectionAndCannotConsumeDelayedAck(bool listenerThrows)
    {
        using var releaseAck = new ManualResetEventSlim();
        using var ackAttempted = new ManualResetEventSlim();
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            Assert.IsTrue(releaseAck.Wait(3000));
            try
            {
                SendAll(socket, ResponseHeader());
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted ||
                                             ex.SocketErrorCode == SocketError.Shutdown)
            {
            }
            finally
            {
                ackAttempted.Set();
            }
            try
            {
                Assert.AreEqual(0, socket.Receive(new byte[PacketBase.HEADER_LENGTH]),
                    "A second request reached the invalidated connection.");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted)
            {
            }
        });
        var robot = new RobotIF();
        bool notificationRan = false;
        bool callbackOperationCompleted = false;
        robot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RobotIF.IsConnected) && !robot.IsConnected)
            {
                notificationRan = true;
                var operation = Task.Run(() => Assert.ThrowsExactly<IOException>(() => robot.WriteCommand("CALLBACK")));
                callbackOperationCompleted = operation.Wait(2000);
                if (listenerThrows)
                    throw new InvalidOperationException("Listener failure.");
            }
        };
        try
        {
            robot.Connect("127.0.0.1", server.Port, 500);
            if (listenerThrows)
            {
                var error = Assert.ThrowsExactly<AggregateException>(() => robot.WriteCommand("FIRST"));
                Assert.HasCount(2, error.InnerExceptions);
                Assert.IsInstanceOfType<SocketException>(error.InnerExceptions[0]);
                Assert.IsInstanceOfType<InvalidOperationException>(error.InnerExceptions[1]);
            }
            else
            {
                Assert.ThrowsExactly<SocketException>(() => robot.WriteCommand("FIRST"));
            }
            Assert.IsFalse(robot.IsConnected);
            Assert.IsTrue(notificationRan);
            Assert.IsTrue(callbackOperationCompleted, "Disconnect notification held the operation lock.");
            releaseAck.Set();
            Assert.IsTrue(ackAttempted.Wait(3000));
            Assert.ThrowsExactly<IOException>(() => robot.WriteCommand("SECOND"));
            server.Complete();
        }
        finally
        {
            releaseAck.Set();
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void RejectedRequestDoesNotInvalidateConnection()
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            var failure = ResponseHeader();
            failure[31] = (byte)MessageType.SHORT_FAILED;
            SendAll(socket, failure);
            var request = ReadRequest(socket);
            Assert.AreEqual((byte)3, request[2]);
            SendAll(socket, ResponseHeader());
        });
        var robot = new RobotIF();
        try
        {
            robot.Connect("127.0.0.1", server.Port, 3000);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => robot.WriteDI(0, true));
            Assert.IsTrue(robot.IsConnected);
            Assert.ThrowsExactly<IOException>(() => robot.WriteCommand("FIRST"));
            Assert.IsTrue(robot.IsConnected);
            robot.WriteCommand("SECOND");
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void ConcurrentDigitalWritesPreserveBothBitsAndSequence()
    {
        using var firstRead = new ManualResetEventSlim();
        using var releaseRead = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();
        using var secondChecked = new ManualResetEventSlim();
        var requests = new List<byte[]>();
        byte bits = 0;
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            for (var i = 0; i < 4; i++)
            {
                var request = ReadRequest(socket);
                requests.Add(request);
                if (request[42] == (byte)ServiceRequestCode.READ_SYS_MEM)
                {
                    if (i == 0)
                    {
                        firstRead.Set();
                        Assert.IsTrue(secondStarted.Wait(3000));
                        Assert.IsFalse(socket.Poll(250000, SelectMode.SelectRead), "Another request overlapped the read.");
                        secondChecked.Set();
                        Assert.IsTrue(releaseRead.Wait(3000));
                    }
                    var response = ResponseHeader();
                    response[44] = bits;
                    SendAll(socket, response);
                }
                else
                {
                    Assert.AreEqual((byte)ServiceRequestCode.WRITE_SYS_MEM, request[42]);
                    bits = request[48];
                    SendAll(socket, ResponseHeader());
                }
            }
        });

        var robot = new RobotIF();
        try
        {
            robot.Connect("127.0.0.1", server.Port, 3000);
            var first = Task.Run(() => robot.WriteDO(1, true));
            Assert.IsTrue(firstRead.Wait(3000));
            var second = Task.Run(() => { secondStarted.Set(); robot.WriteDO(2, true); });
            Assert.IsTrue(secondStarted.Wait(3000));
            Assert.IsTrue(secondChecked.Wait(3000));
            releaseRead.Set();
            Assert.IsTrue(Task.WaitAll(new[] { first, second }, 5000));
            server.Complete();
            Assert.AreEqual((byte)3, bits);
            CollectionAssert.AreEqual(new byte[] { 2, 3, 4, 5 }, requests.Select(r => r[2]).ToArray());
            CollectionAssert.AreEqual(
                new byte[] { (byte)ServiceRequestCode.READ_SYS_MEM, (byte)ServiceRequestCode.WRITE_SYS_MEM,
                    (byte)ServiceRequestCode.READ_SYS_MEM, (byte)ServiceRequestCode.WRITE_SYS_MEM },
                requests.Select(r => r[42]).ToArray());
        }
        finally
        {
            releaseRead.Set();
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void DisconnectInterruptsHandshakeAndClosesClient()
    {
        using var received = new ManualResetEventSlim();
        using var server = new TestServer(socket =>
        {
            ReadRequest(socket);
            received.Set();
            Assert.AreEqual(0, socket.Receive(new byte[1]));
        });
        var robot = new RobotIF();
        var connect = Task.Run(() => Assert.ThrowsExactly<IOException>(
            () => robot.Connect("127.0.0.1", server.Port, 30000)));
        try
        {
            Assert.IsTrue(received.Wait(3000));
            Assert.IsFalse(robot.IsConnected);
            robot.Disconnect();
            Assert.IsTrue(connect.Wait(3000), "Disconnect did not interrupt the handshake.");
            server.Complete();
            Assert.IsFalse(robot.IsConnected);
            robot.Disconnect();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void OperationsStartedDuringHandshakeDoNotRunOnNewConnection()
    {
        using var received = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var server = new TestServer(socket =>
        {
            ReadRequest(socket);
            received.Set();
            Assert.IsTrue(release.Wait(3000));
            SendAll(socket, ResponseHeader());
            ReadRequest(socket);
            SendAll(socket, ResponseHeader());
            var request = ReadRequest(socket);
            Assert.AreEqual((byte)2, request[2]);
            var response = ResponseHeader();
            response[44] = 7;
            SendAll(socket, response);
        });
        var robot = new RobotIF();
        using var started = new ManualResetEventSlim();
        Exception? error = null;
        var queued = new Thread(() =>
        {
            started.Set();
            try { robot.WriteCommand("QUEUED"); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        try
        {
            var connect = Task.Run(() => robot.Connect("127.0.0.1", server.Port, 3000));
            Assert.IsTrue(received.Wait(3000));
            queued.Start();
            Assert.IsTrue(started.Wait(3000));
            Assert.IsTrue(SpinWait.SpinUntil(() => (queued.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 3000));
            release.Set();
            Assert.IsTrue(connect.Wait(3000));
            Assert.IsTrue(queued.Join(3000));
            Assert.IsInstanceOfType<IOException>(error);
            CollectionAssert.AreEqual(new byte[] { 7, 0 }, robot.ReadSNPX(1, 2));
            server.Complete();
        }
        finally
        {
            release.Set();
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void ConcurrentConnectCannotReplaceInProgressConnection()
    {
        using var received = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var server = new TestServer(socket =>
        {
            ReadRequest(socket);
            received.Set();
            Assert.IsTrue(release.Wait(3000));
            SendAll(socket, ResponseHeader());
            ReadRequest(socket);
            SendAll(socket, ResponseHeader());
            ReadRequest(socket);
            SendAll(socket, ResponseHeader());
        });
        var robot = new RobotIF();
        using var started = new ManualResetEventSlim();
        Exception? error = null;
        var second = new Thread(() =>
        {
            started.Set();
            try { robot.Connect("127.0.0.1", server.Port, 3000); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        try
        {
            var first = Task.Run(() => robot.Connect("127.0.0.1", server.Port, 3000));
            Assert.IsTrue(received.Wait(3000));
            second.Start();
            Assert.IsTrue(started.Wait(3000));
            Assert.IsTrue(SpinWait.SpinUntil(() => (second.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 3000));
            release.Set();
            Assert.IsTrue(first.Wait(3000));
            Assert.IsTrue(second.Join(3000));
            Assert.IsInstanceOfType<IOException>(error);
            Assert.IsTrue(robot.IsConnected);
            robot.WriteCommand("VERIFY");
            server.Complete();
        }
        finally
        {
            release.Set();
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void DisconnectInterruptsReadAndQueuedOperationCannotUseNewConnection()
    {
        using var received = new ManualResetEventSlim();
        using var oldServer = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            received.Set();
            Assert.AreEqual(0, socket.Receive(new byte[1]));
        });
        using var newServer = new TestServer(socket =>
        {
            Handshake(socket);
            var request = ReadRequest(socket);
            Assert.AreEqual((byte)2, request[2]);
            var response = ResponseHeader();
            response[44] = 42;
            SendAll(socket, response);
        });
        var robot = new RobotIF();
        using var queuedStarted = new ManualResetEventSlim();
        Exception? queuedError = null;
        var queued = new Thread(() =>
        {
            queuedStarted.Set();
            try { robot.WriteCommand("QUEUED"); }
            catch (Exception ex) { queuedError = ex; }
        }) { IsBackground = true };
        try
        {
            robot.Connect("127.0.0.1", oldServer.Port, 30000);
            var blocked = Task.Run(() => Assert.Throws<Exception>(() => robot.ReadSNPX(1, 2)));
            Assert.IsTrue(received.Wait(3000));
            queued.Start();
            Assert.IsTrue(queuedStarted.Wait(3000));
            Assert.IsTrue(SpinWait.SpinUntil(() => (queued.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 3000));
            robot.Disconnect();
            Assert.IsTrue(blocked.Wait(3000), "Disconnect did not interrupt the read.");
            robot.Connect("127.0.0.1", newServer.Port, 3000);
            Assert.IsTrue(queued.Join(3000));
            Assert.IsInstanceOfType<IOException>(queuedError);
            CollectionAssert.AreEqual(new byte[] { 42, 0 }, robot.ReadSNPX(1, 2));
            oldServer.Complete();
            newServer.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void FailedHandshakesCloseClientsAndAllowReconnect()
    {
        var robot = new RobotIF();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                using var server = new TestServer(socket =>
                {
                    ReadRequest(socket);
                    var response = ResponseHeader();
                    response[31] = (byte)MessageType.SHORT_FAILED;
                    SendAll(socket, response);
                    Assert.AreEqual(0, socket.Receive(new byte[1]));
                });
                Assert.ThrowsExactly<IOException>(() => robot.Connect("127.0.0.1", server.Port, 3000));
                server.Complete();
                Assert.IsFalse(robot.IsConnected);
                robot.Disconnect();
            }
            using var success = new TestServer(Handshake);
            robot.Connect("127.0.0.1", success.Port, 3000);
            success.Complete();
            robot.Disconnect();
            robot.Disconnect();
            Assert.IsFalse(robot.IsConnected);
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void ConnectFailuresAllowRepeatedReconnect()
    {
        using var server = new TestServer(Handshake);
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var closedPort = (ushort)((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var robot = new RobotIF();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.ThrowsExactly<IOException>(() => robot.Connect(null!, server.Port, 3000));
                Assert.ThrowsExactly<IOException>(() => robot.Connect("127.0.0.1", closedPort, 3000));
                Assert.IsFalse(robot.IsConnected);
                robot.Disconnect();
            }
            robot.Connect("127.0.0.1", server.Port, 3000);
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    private static void Handshake(Socket socket)
    {
        ReadRequest(socket);
        SendAll(socket, ResponseHeader());
        ReadRequest(socket);
        SendAll(socket, ResponseHeader());
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
                Assert.IsFalse(robot.IsConnected);
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
