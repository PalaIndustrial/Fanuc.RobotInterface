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
            var request = ReadRequest(socket);
            Assert.AreEqual((byte)MessageType.SHORT, request[31]);
            SendAll(socket, ResponseHeader(expected.Length));
            SendAll(socket, expected, 0, 3);
            Thread.Sleep(200);
            SendAll(socket, expected, 3, expected.Length - 3);
        });

        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            CollectionAssert.AreEqual(expected, robot.ReadSNPX(1, (ushort)expected.Length));
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    [DataRow("DI", false, 7)]
    [DataRow("DI", true, 7)]
    [DataRow("GI", false, 8)]
    [DataRow("GI", true, 8)]
    [DataRow("SNPX", false, 8)]
    [DataRow("SNPX", true, 8)]
    public void ReadRejectsInsufficientPayload(string kind, bool extended, int requiredBytes)
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            SendAll(socket, ResponseHeader(extended ? 4 : 0));
            if (extended)
                SendAll(socket, new byte[] { 1, 2, 3, 4 });
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            Action read = kind switch
            {
                "DI" => () => robot.ReadDI(2, 49),
                "GI" => () => robot.ReadGI(1, 4),
                _ => () => robot.ReadSNPX(1, 8)
            };
            var error = Assert.ThrowsExactly<IOException>(read);
            StringAssert.Contains(error.Message, $"returned {(extended ? 4 : 6)} payload bytes");
            StringAssert.Contains(error.Message, $"expected at least {requiredBytes}");
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    [DataRow("DI", 1)]
    [DataRow("GI", 4)]
    [DataRow("SNPX", 4)]
    public void ReadAcceptsAndTracesExcessExtendedPayload(string kind, int requiredBytes)
    {
        var payload = new byte[] { 0x55, 0xaa, 0x33, 0x44, 0x66, 0x77, 0x99, 0x88 };
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            SendAll(socket, ResponseHeader(payload.Length));
            SendAll(socket, payload);
        });
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            switch (kind)
            {
                case "DI":
                    CollectionAssert.AreEqual(new[] { true, false, true, false, true, false, true, false }, robot.ReadDI(1, 8));
                    break;
                case "GI":
                    CollectionAssert.AreEqual(new ushort[] { 0xaa55, 0x4433 }, robot.ReadGI(1, 2));
                    break;
                default:
                    CollectionAssert.AreEqual(payload.Take(4).ToArray(), robot.ReadSNPX(1, 4));
                    break;
            }
            Trace.Flush();
            StringAssert.Contains(output.ToString(), $"returned 8 payload bytes; expected {requiredBytes}");
            server.Complete();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void ShortResponsePaddingDoesNotProduceExcessWarning()
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            var response = ResponseHeader();
            response[44] = 7;
            response[45] = 9;
            SendAll(socket, response);
        });
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            CollectionAssert.AreEqual(new byte[] { 7, 9 }, robot.ReadSNPX(1, 2));
            Trace.Flush();
            Assert.AreEqual("", output.ToString());
            server.Complete();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            robot.Disconnect();
        }
    }

    [TestMethod]
    [DataRow("DI", 0xc0, 0)]
    [DataRow("GI", 0x80, 4)]
    [DataRow("SNPX", 0x00, 0)]
    [DataRow("DI", 0xd4, 4)]
    [DataRow("GI", 0xd4, 4)]
    [DataRow("SNPX", 0xd4, 4)]
    public void ReadRejectsUnexpectedAckAfterReceivingEntireFrame(string kind, int messageType, int extraLength)
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            var response = ResponseHeader(extraLength);
            response[31] = (byte)messageType;
            SendAll(socket, response);
            if (extraLength > 0)
                SendAll(socket, new byte[] { 1, 2, 3, 4 });

            ReadRequest(socket);
            var nextResponse = ResponseHeader();
            nextResponse[44] = 42;
            SendAll(socket, nextResponse);
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            Action read = kind switch
            {
                "DI" => () => robot.ReadDI(1, 8),
                "GI" => () => robot.ReadGI(1, 1),
                _ => () => robot.ReadSNPX(1, 2)
            };
            var error = Assert.ThrowsExactly<IOException>(read);
            StringAssert.Contains(error.Message, $"unexpected response type 0x{messageType:X2}");
            CollectionAssert.AreEqual(new byte[] { 42, 0 }, robot.ReadSNPX(1, 2));
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void LongAckWithoutExtraPayloadDoesNotUseInlineBytes()
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            var response = ResponseHeader();
            response[31] = (byte)MessageType.LONG_ACK;
            response[44] = 42;
            SendAll(socket, response);
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            var error = Assert.ThrowsExactly<IOException>(() => robot.ReadSNPX(1, 2));
            StringAssert.Contains(error.Message, "returned 0 payload bytes; expected at least 2");
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    [DataRow("DI")]
    [DataRow("GI")]
    [DataRow("SNPX")]
    public void ReadRejectsFailureResponse(string kind)
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            var response = ResponseHeader();
            response[31] = (byte)MessageType.SHORT_FAILED;
            SendAll(socket, response);
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            Action read = kind switch
            {
                "DI" => () => robot.ReadDI(1, 8),
                "GI" => () => robot.ReadGI(1, 1),
                _ => () => robot.ReadSNPX(1, 2)
            };
            var error = Assert.ThrowsExactly<IOException>(read);
            StringAssert.Contains(error.Message, "Wrong data received");
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

        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            var error = Assert.ThrowsExactly<IOException>(
                () => robot.Connect(3000));
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

        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
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
    public void InvalidRangesAndPayloadsAreRejectedBeforeSending()
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);
            var request = ReadRequest(socket);
            Assert.AreEqual((byte)2, request[2]);
            Assert.AreEqual((byte)ServiceRequestCode.WRITE_SYS_MEM, request[42]);
            Assert.AreEqual((byte)'O', request[48]);
            Assert.AreEqual((byte)'K', request[49]);
            SendAll(socket, ResponseHeader());
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            Action[] invalid =
            [
                () => robot.ReadDI(0, 1),
                () => robot.ReadDI(65537, 1),
                () => robot.ReadDI(int.MaxValue, 1),
                () => robot.ReadDI(65536, 2),
                () => robot.ReadDI(1, 65535),
                () => robot.ReadDO(65537, 1),
                () => robot.ReadGI(0, 1),
                () => robot.ReadGI(65537, 1),
                () => robot.ReadGI(65536, 2),
                () => robot.ReadGI(1, 32768),
                () => robot.ReadGO(65537, 1),
                () => robot.ReadSNPX(0, 2),
                () => robot.ReadSNPX(0, 0),
                () => robot.ReadSNPX(65537, 2),
                () => robot.ReadSNPX(65537, 0),
                () => robot.ReadSNPX(65536, 4),
                () => robot.ReadSNPX(1, 1),
                () => robot.ReadSNPX(1, 65535),
                () => robot.WriteDI(65536, true, true),
                () => robot.WriteDO(65537, true),
                () => robot.WriteGI(65536, 1, 2),
                () => robot.WriteGO(1, new ushort[32768]),
                () => robot.WriteSNPX(0, 1, 2),
                () => robot.WriteSNPX(0),
                () => robot.WriteSNPX(65537, 1, 2),
                () => robot.WriteSNPX(65537),
                () => robot.WriteSNPX(65536, 1, 2, 3, 4),
                () => robot.WriteSNPX(1, 1),
                () => robot.WriteSNPX(1, new byte[65535]),
                () => robot.WriteSNPX(1, new byte[65536]),
                () => robot.WriteCommand(new string('A', 65536)),
                () => robot.ReadUI(0, 1),
                () => robot.ReadUI(-5999, 1),
                () => robot.ReadRDO(int.MaxValue, 1),
                () => robot.ReadAI(0, 1),
                () => robot.ReadPmcD(55537, 1),
                () => robot.WriteUI(0, true),
                () => robot.WritePmcK(55537, true),
                () => robot.WriteAO(0, 1),
                () => robot.WriteAI(64537, 1)
            ];
            foreach (var operation in invalid)
                Assert.ThrowsExactly<ArgumentOutOfRangeException>(operation);

            var offsetRangeError = Assert.ThrowsExactly<IOException>(() => robot.ReadUI(59536, 2));
            Assert.IsInstanceOfType<ArgumentOutOfRangeException>(offsetRangeError.InnerException);

            Assert.AreEqual(0, robot.ReadDI(65536, 0).Length);
            Assert.AreEqual(0, robot.ReadGI(65536, 0).Length);
            Assert.AreEqual(0, robot.ReadSNPX(65536, 0).Length);
            robot.WriteDI(65536);
            robot.WriteGI(65536);
            robot.WriteSNPX(65536);
            robot.WriteCommand("OK");
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void BoundaryReadsPreserveOneBasedAddressesAndCounts()
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);

            var digital = ReadRequest(socket);
            Assert.AreEqual((byte)0xf8, digital[44]);
            Assert.AreEqual((byte)0xff, digital[45]);
            Assert.AreEqual((byte)8, digital[46]);
            var digitalResponse = ResponseHeader();
            digitalResponse[44] = 0x80;
            SendAll(socket, digitalResponse);

            var group = ReadRequest(socket);
            Assert.AreEqual((byte)0xff, group[44]);
            Assert.AreEqual((byte)0xff, group[45]);
            Assert.AreEqual((byte)1, group[46]);
            var groupResponse = ResponseHeader();
            groupResponse[44] = 0x34;
            groupResponse[45] = 0x12;
            SendAll(socket, groupResponse);

            var snpx = ReadRequest(socket);
            Assert.AreEqual((byte)0xff, snpx[44]);
            Assert.AreEqual((byte)0xff, snpx[45]);
            Assert.AreEqual((byte)1, snpx[46]);
            var snpxResponse = ResponseHeader();
            snpxResponse[44] = 0x56;
            snpxResponse[45] = 0x78;
            SendAll(socket, snpxResponse);

            var ui = ReadRequest(socket);
            Assert.AreEqual((byte)0x70, ui[44]);
            Assert.AreEqual((byte)0x17, ui[45]);
            var uiResponse = ResponseHeader();
            uiResponse[44] = 1;
            SendAll(socket, uiResponse);

            var lastUi = ReadRequest(socket);
            Assert.AreEqual((byte)0xf8, lastUi[44]);
            Assert.AreEqual((byte)0xff, lastUi[45]);
            var lastUiResponse = ResponseHeader();
            lastUiResponse[44] = 0x80;
            SendAll(socket, lastUiResponse);

            var largeGroup = ReadRequest(socket);
            Assert.AreEqual((byte)0xff, largeGroup[46]);
            Assert.AreEqual((byte)0x7f, largeGroup[47]);
            SendAll(socket, ResponseHeader(65534));
            SendAll(socket, new byte[65534]);

            var largeSnpx = ReadRequest(socket);
            Assert.AreEqual((byte)0x01, largeSnpx[44]);
            Assert.AreEqual((byte)0x80, largeSnpx[45]);
            Assert.AreEqual((byte)0xff, largeSnpx[46]);
            Assert.AreEqual((byte)0x7f, largeSnpx[47]);
            SendAll(socket, ResponseHeader(65534));
            SendAll(socket, new byte[65534]);
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            CollectionAssert.AreEqual(new[] { true }, robot.ReadDI(65536, 1));
            CollectionAssert.AreEqual(new ushort[] { 0x1234 }, robot.ReadGI(65536, 1));
            CollectionAssert.AreEqual(new byte[] { 0x56, 0x78 }, robot.ReadSNPX(65536, 2));
            CollectionAssert.AreEqual(new[] { true }, robot.ReadUI(1, 1));
            CollectionAssert.AreEqual(new[] { true }, robot.ReadUI(59536, 1));
            Assert.AreEqual(32767, robot.ReadGI(1, 32767).Length);
            Assert.AreEqual(65534, robot.ReadSNPX(32770, 65534).Length);
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void BoundaryWritesPreserveAddressesAndMaximumPayloads()
    {
        using var server = new TestServer(socket =>
        {
            Handshake(socket);

            var shortSnpx = ReadRequest(socket);
            Assert.AreEqual((byte)0xff, shortSnpx[44]);
            Assert.AreEqual((byte)0xff, shortSnpx[45]);
            Assert.AreEqual((byte)1, shortSnpx[46]);
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, shortSnpx.Skip(48).Take(2).ToArray());
            SendAll(socket, ResponseHeader());

            var largeSnpx = ReadRequest(socket);
            Assert.AreEqual(65534, PacketBase.ExtraLengthFromHeaderBuffer(largeSnpx));
            Assert.AreEqual((byte)0x01, largeSnpx[52]);
            Assert.AreEqual((byte)0x80, largeSnpx[53]);
            Assert.AreEqual((byte)0xff, largeSnpx[54]);
            Assert.AreEqual((byte)0x7f, largeSnpx[55]);
            SendAll(socket, ResponseHeader());

            var largeGroup = ReadRequest(socket);
            Assert.AreEqual(65534, PacketBase.ExtraLengthFromHeaderBuffer(largeGroup));
            Assert.AreEqual((byte)0x01, largeGroup[52]);
            Assert.AreEqual((byte)0x80, largeGroup[53]);
            Assert.AreEqual((byte)0xff, largeGroup[54]);
            Assert.AreEqual((byte)0x7f, largeGroup[55]);
            SendAll(socket, ResponseHeader());

            var digitalRead = ReadRequest(socket);
            Assert.AreEqual((byte)0xf8, digitalRead[44]);
            Assert.AreEqual((byte)0xff, digitalRead[45]);
            var digitalResponse = ResponseHeader();
            digitalResponse[44] = 0x55;
            SendAll(socket, digitalResponse);

            var digitalWrite = ReadRequest(socket);
            Assert.AreEqual((byte)0xf8, digitalWrite[44]);
            Assert.AreEqual((byte)0xff, digitalWrite[45]);
            Assert.AreEqual((byte)8, digitalWrite[46]);
            Assert.AreEqual((byte)0xd5, digitalWrite[48]);
            SendAll(socket, ResponseHeader());

            var command = ReadRequest(socket);
            Assert.AreEqual(65535, PacketBase.ExtraLengthFromHeaderBuffer(command));
            Assert.AreEqual((byte)0xff, command[54]);
            Assert.AreEqual((byte)0xff, command[55]);
            Assert.IsTrue(command.Skip(PacketBase.HEADER_LENGTH).All(b => b == (byte)'A'));
            SendAll(socket, ResponseHeader());
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
            robot.WriteSNPX(65536, 1, 2);
            robot.WriteSNPX(32770, new byte[65534]);
            robot.WriteGI(32770, new ushort[32767]);
            robot.WriteDO(65536, true);
            robot.WriteCommand(new string('A', 65535));
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void RequestWithoutConnectionFailsClearly()
    {
        var robot = new RobotIF("localhost");
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
        var robot = new RobotIF("127.0.0.1", server.Port);
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
            robot.Connect(500);
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
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
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

        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            robot.Connect(3000);
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
        var robot = new RobotIF("127.0.0.1", server.Port);
        var connect = Task.Run(() => Assert.ThrowsExactly<IOException>(
            () => robot.Connect(30000)));
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
        var robot = new RobotIF("127.0.0.1", server.Port);
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
            var connect = Task.Run(() => robot.Connect(3000));
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
        var robot = new RobotIF("127.0.0.1", server.Port);
        using var started = new ManualResetEventSlim();
        Exception? error = null;
        var second = new Thread(() =>
        {
            started.Set();
            try { robot.Connect(3000); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        try
        {
            var first = Task.Run(() => robot.Connect(3000));
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
        using var server = new TestServer(0, socket =>
        {
            Handshake(socket);
            ReadRequest(socket);
            received.Set();
            Assert.AreEqual(0, socket.Receive(new byte[1]));
        }, socket =>
        {
            Handshake(socket);
            var request = ReadRequest(socket);
            Assert.AreEqual((byte)2, request[2]);
            var response = ResponseHeader();
            response[44] = 42;
            SendAll(socket, response);
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
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
            robot.Connect(30000);
            var blocked = Task.Run(() => Assert.Throws<Exception>(() => robot.ReadSNPX(1, 2)));
            Assert.IsTrue(received.Wait(3000));
            queued.Start();
            Assert.IsTrue(queuedStarted.Wait(3000));
            Assert.IsTrue(SpinWait.SpinUntil(() => (queued.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 3000));
            robot.Disconnect();
            Assert.IsTrue(blocked.Wait(3000), "Disconnect did not interrupt the read.");
            robot.Connect(3000);
            Assert.IsTrue(queued.Join(3000));
            Assert.IsInstanceOfType<IOException>(queuedError);
            CollectionAssert.AreEqual(new byte[] { 42, 0 }, robot.ReadSNPX(1, 2));
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void FailedHandshakesCloseClientsAndAllowReconnect()
    {
        void FailHandshake(Socket socket)
        {
            ReadRequest(socket);
            var response = ResponseHeader();
            response[31] = (byte)MessageType.SHORT_FAILED;
            SendAll(socket, response);
            Assert.AreEqual(0, socket.Receive(new byte[1]));
        }
        using var server = new TestServer(0, FailHandshake, FailHandshake, FailHandshake, Handshake);
        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.ThrowsExactly<IOException>(() => robot.Connect(3000));
                Assert.IsFalse(robot.IsConnected);
                robot.Disconnect();
            }
            robot.Connect(3000);
            server.Complete();
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
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var closedPort = (ushort)((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var robot = new RobotIF("127.0.0.1", closedPort);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.ThrowsExactly<ArgumentNullException>(() => new RobotIF(null!, closedPort));
                Assert.ThrowsExactly<IOException>(() => robot.Connect(3000));
                Assert.IsFalse(robot.IsConnected);
                robot.Disconnect();
            }
            using var server = new TestServer(Handshake, closedPort);
            robot.Connect(3000);
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void TcpConnectTimeoutClosesAttemptAndAllowsReconnect()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        var probes = new List<TcpClient>();
        Task? pending = null;
        listener.Start(1);
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        var robot = new RobotIF("127.0.0.1", port);
        var noTimeout = new RobotIF("127.0.0.1", port);
        var racing = new RobotIF("127.0.0.1", port);
        var wrapper = new ExRobotIF(new RobotIF("127.0.0.1", port));
        try
        {
            for (var i = 0; i < 32; i++)
            {
                var probe = new TcpClient();
                probes.Add(probe);
                var connect = probe.ConnectAsync(IPAddress.Loopback, port);
                if (Task.WhenAny(connect, Task.Delay(500)).GetAwaiter().GetResult() != connect)
                {
                    pending = connect;
                    break;
                }
                if (connect.IsFaulted || connect.IsCanceled)
                {
                    _ = connect.Exception;
                    Assert.Inconclusive("This system rejects connections instead of leaving them pending when the loopback backlog fills.");
                }
                connect.GetAwaiter().GetResult();
            }
            if (pending == null)
                Assert.Inconclusive("The loopback listener's connection backlog did not fill.");

            var watch = Stopwatch.StartNew();
            var clientField = typeof(RobotIF).GetField("TcpClient", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(clientField);
            Socket? attemptedSocket = null;
            var attempt = Task.Run(() => Assert.ThrowsExactly<IOException>(
                () => robot.Connect(1000)));
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                var client = clientField.GetValue(robot) as TcpClient;
                attemptedSocket = client?.Client;
                return attemptedSocket != null;
            }, 3000), "The TCP client was not published.");
            var error = attempt.GetAwaiter().GetResult();
            watch.Stop();
            Assert.IsInstanceOfType<TimeoutException>(error.InnerException);
            Assert.IsTrue(watch.ElapsedMilliseconds >= 500, $"Connection timed out too early: {watch.Elapsed}.");
            Assert.IsTrue(watch.ElapsedMilliseconds < 5000, $"Connection timeout took too long: {watch.Elapsed}.");
            Assert.IsFalse(robot.IsConnected);
            Assert.IsNotNull(attemptedSocket);
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = attemptedSocket.Available);

            var wrapperError = Assert.ThrowsExactlyAsync<IOException>(
                () => wrapper.ConnectAsync(300)).GetAwaiter().GetResult();
            Assert.IsInstanceOfType<TimeoutException>(wrapperError.InnerException);
            Assert.IsFalse(wrapper.IsConnected);
            Assert.IsNotNull(wrapper.LastError);

            var waiting = Task.Run(() =>
            {
                return Assert.ThrowsExactly<IOException>(() => noTimeout.Connect(0));
            });
            Socket? waitingSocket = null;
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                var client = clientField.GetValue(noTimeout) as TcpClient;
                waitingSocket = client?.Client;
                return waitingSocket != null;
            }, 3000), "The zero-timeout TCP client was not published.");
            Assert.IsFalse(waiting.Wait(400), "Zero timeout should not impose a TCP connect deadline.");
            noTimeout.Disconnect();
            Assert.IsTrue(waiting.Wait(5000), "Disconnect did not interrupt the pending TCP connection.");
            Assert.IsFalse(noTimeout.IsConnected);
            Assert.IsNotNull(waitingSocket);
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = waitingSocket.Available);

            Socket? racingSocket = null;
            var disposeField = typeof(RobotIF).GetField("ClientDisposeLock", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(disposeField);
            var disposeLock = disposeField.GetValue(racing);
            Assert.IsNotNull(disposeLock);
            Task<IOException>? racingAttempt = null;
            Task? disconnect = null;
            Monitor.Enter(disposeLock);
            try
            {
                racingAttempt = Task.Run(() => Assert.ThrowsExactly<IOException>(
                    () => racing.Connect(500)));
                Assert.IsTrue(SpinWait.SpinUntil(() =>
                {
                    var client = clientField.GetValue(racing) as TcpClient;
                    racingSocket = client?.Client;
                    return racingSocket != null;
                }, 3000), "The racing TCP client was not published.");
                disconnect = Task.Run(racing.Disconnect);
                Assert.IsTrue(SpinWait.SpinUntil(() => clientField.GetValue(racing) == null, 3000),
                    "Disconnect did not detach the TCP client.");
                Assert.IsFalse(racingAttempt.Wait(1500), "Connect returned before client disposal completed.");
            }
            finally
            {
                Monitor.Exit(disposeLock);
            }
            Assert.IsNotNull(racingAttempt);
            Assert.IsNotNull(disconnect);
            Assert.IsTrue(Task.WaitAll(new Task[] { racingAttempt, disconnect }, 5000));
            Assert.IsNotNull(racingSocket);
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = racingSocket.Available);

            listener.Stop();
            foreach (var probe in probes)
                probe.Dispose();
            using var server = new TestServer(port, Handshake, socket =>
            {
                Handshake(socket);
                ReadRequest(socket);
                SendAll(socket, ResponseHeader());
            });
            robot.Connect(3000);
            Assert.IsTrue(robot.IsConnected);
            wrapper.ConnectAsync(3000).GetAwaiter().GetResult();
            Assert.IsTrue(wrapper.IsConnected);
            Assert.IsNull(wrapper.LastError);
            server.Complete();
        }
        finally
        {
            wrapper.Disconnect();
            racing.Disconnect();
            noTimeout.Disconnect();
            robot.Disconnect();
            listener.Stop();
            foreach (var probe in probes)
                probe.Dispose();
            if (pending != null)
                _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
    }

    [TestMethod]
    [DataRow("127.0.0.1", 0)]
    [DataRow("localhost", 3000)]
    public void ConnectSupportsIpAndHostnameWithConfiguredTimeout(string host, int timeout)
    {
        using var server = new TestServer(Handshake);
        var robot = new RobotIF(host, server.Port);
        try
        {
            robot.Connect(timeout);
            Assert.IsTrue(robot.IsConnected);
            server.Complete();
        }
        finally
        {
            robot.Disconnect();
        }
    }

    [TestMethod]
    public void FailedHandshakeWaitsForConcurrentDisconnectDisposal()
    {
        using var requestReceived = new ManualResetEventSlim();
        using var releaseResponse = new ManualResetEventSlim();
        using var responseSent = new ManualResetEventSlim();
        using var server = new TestServer(socket =>
        {
            ReadRequest(socket);
            requestReceived.Set();
            Assert.IsTrue(releaseResponse.Wait(5000));
            SendAll(socket, ResponseHeader());
            responseSent.Set();
            Assert.AreEqual(0, socket.Receive(new byte[1]));
        });
        var robot = new RobotIF("127.0.0.1", server.Port);
        var clientField = typeof(RobotIF).GetField("TcpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        var disposeField = typeof(RobotIF).GetField("ClientDisposeLock", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(clientField);
        Assert.IsNotNull(disposeField);
        var disposeLock = disposeField.GetValue(robot);
        Assert.IsNotNull(disposeLock);
        Task? disconnect = null;
        var connect = Task.Run(() => Assert.ThrowsExactly<IOException>(
            () => robot.Connect(3000)));
        try
        {
            Assert.IsTrue(requestReceived.Wait(3000));
            Monitor.Enter(disposeLock);
            try
            {
                disconnect = Task.Run(robot.Disconnect);
                Assert.IsTrue(SpinWait.SpinUntil(() => clientField.GetValue(robot) == null, 3000));
                releaseResponse.Set();
                Assert.IsTrue(responseSent.Wait(3000));
                Assert.IsFalse(connect.Wait(500), "Connect returned before client disposal completed.");
            }
            finally
            {
                Monitor.Exit(disposeLock);
            }
            Assert.IsNotNull(disconnect);
            Assert.IsTrue(Task.WaitAll(new Task[] { connect, disconnect }, 5000));
            Assert.IsFalse(robot.IsConnected);
            server.Complete();
        }
        finally
        {
            releaseResponse.Set();
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

        var robot = new RobotIF("127.0.0.1", server.Port);
        try
        {
            if (duringRead)
            {
                robot.Connect(3000);
                var error = Assert.ThrowsExactly<EndOfStreamException>(() => robot.ReadSNPX(1, 8));
                StringAssert.Contains(error.Message, "3 of 8");
                Assert.IsFalse(robot.IsConnected);
            }
            else
            {
                var error = Assert.ThrowsExactly<IOException>(
                    () => robot.Connect(3000));
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
        header[31] = (byte)(extraLength == 0 ? MessageType.SHORT_ACK : MessageType.LONG_ACK);
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
        private readonly TcpListener _listener;
        private readonly Task _task;

        public ushort Port { get; }

        public TestServer(Action<Socket> handler, ushort port = 0)
            : this(port, handler)
        {
        }

        public TestServer(ushort port, params Action<Socket>[] handlers)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            Port = (ushort)((IPEndPoint)_listener.LocalEndpoint).Port;
            _task = Task.Run(() =>
            {
                foreach (var handler in handlers)
                {
                    using var socket = _listener.AcceptSocket();
                    socket.NoDelay = true;
                    socket.ReceiveTimeout = 3000;
                    socket.SendTimeout = 3000;
                    handler(socket);
                }
            });
        }

        public void Complete() => _task.GetAwaiter().GetResult();

        public void Dispose() => _listener.Stop();
    }
}
