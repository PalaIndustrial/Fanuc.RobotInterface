using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Fanuc.RobotInterface;
using Fanuc.SRTP;

namespace Fanuc.RobotInterface.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ExRobotIFLifecycleTests
{
    private const int TimeoutMs = 5000;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
#pragma warning disable MSTEST0045 // The transport can block in synchronous socket calls.
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task InitializationFailureRollsBackAndReconnects(bool failAssignment)
    {
        var poll = Signal();
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG", failAssignment);
            if (failAssignment)
                Command(socket, "SETASG 1 2 R[1] 0", false);
            ExpectClosed(socket);
        }, socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG");
            Command(socket, "SETASG 1 2 R[1] 0");
            ExpectPoll(socket);
            poll.TrySetResult(true);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        _ = robot.R[1];
        try
        {
            await ExpectFault(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            Assert.IsFalse(robot.IsConnected);
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            Assert.IsTrue(robot.IsConnected);
            await Bounded(poll.Task);
        }
        finally
        {
            await Bounded(Task.Run(robot.Disconnect));
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task InitializationFailureNotifiesAndExposesErrorThroughInterface()
    {
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG", false);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        IExRobotIF api = robot;
        var notifications = new ConcurrentQueue<string?>();
        robot.PropertyChanged += (_, e) => notifications.Enqueue(e.PropertyName);
        try
        {
            await ExpectFault(api.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            Assert.IsFalse(api.IsConnected);
            Assert.IsNotNull(api.LastError);
            StringAssert.Contains(api.LastError.ToString(), "Write");
            Assert.IsTrue(notifications.Contains(nameof(ExRobotIF.LastError)));
            Assert.IsTrue(notifications.Contains(nameof(ExRobotIF.IsConnected)));
        }
        finally
        {
            await Bounded(Task.Run(robot.Disconnect));
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task ReconnectRebuildsAssignmentsFromFirstOffset()
    {
        var firstPoll = Signal();
        var secondPoll = Signal();
        using var server = new Controller(socket => Session(socket, firstPoll), socket => Session(socket, secondPoll));
        var robot = new ExRobotIF(new RobotIF());
        _ = robot.R[1];
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(firstPoll.Task);
            await Bounded(Task.Run(robot.Disconnect));
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(secondPoll.Task);
        }
        finally
        {
            await Bounded(Task.Run(robot.Disconnect));
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task DisconnectDuringInitializationInterruptsAndAllowsReconnect()
    {
        var initializing = Signal();
        var poll = Signal();
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            AssertCommand(ReadRequest(socket), "CLRASG");
            initializing.TrySetResult(true);
            ExpectClosed(socket);
        }, socket => Session(socket, poll));
        var robot = new ExRobotIF(new RobotIF());
        _ = robot.R[1];
        try
        {
            var connecting = robot.ConnectAsync("127.0.0.1", server.Port, 30000);
            await Bounded(initializing.Task);
            await Bounded(Task.Run(robot.Disconnect));
            await ExpectUnsuccessful(connecting);
            Assert.IsFalse(robot.IsConnected);
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(poll.Task);
        }
        finally
        {
            await Bounded(Task.Run(robot.Disconnect));
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task WriteCompletesOnlyAfterAcknowledgment()
    {
        var poll = Signal();
        var releasePoll = new ManualResetEventSlim();
        var write = Signal();
        var nextPoll = Signal();
        var releaseWrite = new ManualResetEventSlim();
        using var server = new Controller(socket =>
        {
            InitializeRegister(socket);
            ExpectPoll(socket);
            poll.TrySetResult(true);
            Wait(releasePoll);
            Respond(socket);
            while (true)
            {
                var request = ReadRequest(socket);
                if (IsRegisterWrite(request))
                {
                    CollectionAssert.AreEqual(BitConverter.GetBytes(42f), request.Skip(48).Take(4).ToArray());
                    write.TrySetResult(true);
                    Wait(releaseWrite);
                    Respond(socket);
                    break;
                }
                AssertPoll(request);
                Respond(socket);
            }
            ExpectPoll(socket);
            nextPoll.TrySetResult(true);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = robot.R[1];
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(poll.Task);
            var pending = holder.WriteAsync(42f);
            releasePoll.Set();
            await Bounded(write.Task);
            Assert.IsFalse(pending.IsCompleted, "Write completed before the controller acknowledged it.");
            releaseWrite.Set();
            await Bounded(pending);
            await Bounded(nextPoll.Task);
        }
        finally
        {
            releasePoll.Set();
            releaseWrite.Set();
            await Bounded(Task.Run(robot.Disconnect));
            releasePoll.Dispose();
            releaseWrite.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task QueuedHolderWritesSendBothValuesAndCompleteOnTheirOwnAcknowledgments()
    {
        var initialPoll = Signal();
        var firstWrite = Signal();
        var secondWrite = Signal();
        var finalPoll = Signal();
        var releasePoll = new ManualResetEventSlim();
        var releaseFirst = new ManualResetEventSlim();
        var releaseSecond = new ManualResetEventSlim();
        using var server = new Controller(socket =>
        {
            InitializeRegister(socket);
            ExpectPoll(socket);
            initialPoll.TrySetResult(true);
            Wait(releasePoll);
            Respond(socket);
            ExpectRegisterWrite(socket, 11f);
            firstWrite.TrySetResult(true);
            Wait(releaseFirst);
            Respond(socket);
            ExpectRegisterWrite(socket, 22f);
            secondWrite.TrySetResult(true);
            Wait(releaseSecond);
            Respond(socket);
            ExpectPoll(socket);
            finalPoll.TrySetResult(true);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = robot.R[1];
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(initialPoll.Task);
            var first = holder.WriteAsync(11f);
            releasePoll.Set();
            await Bounded(firstWrite.Task);
            Assert.IsFalse(first.IsCompleted);
            var second = holder.WriteAsync(22f);
            Assert.IsFalse(second.IsCompleted);
            releaseFirst.Set();
            await Bounded(secondWrite.Task);
            await Bounded(first);
            Assert.IsFalse(second.IsCompleted, "The second write completed before its own acknowledgment.");
            releaseSecond.Set();
            await Bounded(second);
            await Bounded(finalPoll.Task);
        }
        finally
        {
            releasePoll.Set();
            releaseFirst.Set();
            releaseSecond.Set();
            await Bounded(Task.Run(robot.Disconnect));
            releasePoll.Dispose();
            releaseFirst.Dispose();
            releaseSecond.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task PositionWriteSnapshotsBeforeWaitingForBlockedPoll()
    {
        var poll = Signal();
        var nextPoll = Signal();
        var releasePoll = new ManualResetEventSlim();
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG");
            Command(socket, "SETASG 1 50 PR[1] 0.0");
            ExpectPoll(socket);
            poll.TrySetResult(true);
            Wait(releasePoll);
            RespondPosition(socket);
            while (true)
            {
                var request = ReadRequest(socket);
                if (request[50] == (byte)ServiceRequestCode.WRITE_SYS_MEM &&
                    request[51] == (byte)SegmentSelector.WORD_R)
                {
                    Assert.AreEqual(26, request[52] | request[53] << 8);
                    var expected = new byte[38];
                    Array.Copy(BitConverter.GetBytes(12.5f), 0, expected, 0, 4);
                    Array.Copy(BitConverter.GetBytes(-8.25f), 0, expected, 32, 4);
                    CollectionAssert.AreEqual(expected, request.Skip(PacketBase.HEADER_LENGTH).ToArray());
                    Respond(socket);
                    break;
                }
                AssertPoll(request);
                RespondPosition(socket);
            }
            ExpectPoll(socket);
            nextPoll.TrySetResult(true);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = robot.PR[1];
        var position = new Position { Joint = new PositionJoint { J1 = 12.5f, J9 = -8.25f } };
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(poll.Task);
            var pending = holder.WriteAsync(position);
            Assert.IsFalse(pending.IsCompleted, "Write should be queued behind the blocked poll.");
            position.Joint.J1 = 99f;
            position.Joint.J9 = 99f;
            position.Cartisian = new PositionCartisian { X = 123f };
            releasePoll.Set();
            await Bounded(pending);
            await Bounded(nextPoll.Task);
        }
        finally
        {
            releasePoll.Set();
            await Bounded(Task.Run(robot.Disconnect));
            releasePoll.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task RawWritesSnapshotArraysBeforeWaitingForScheduler()
    {
        var blockedRead = Signal();
        var releaseRead = new ManualResetEventSlim();
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG");
            var read = ReadRequest(socket);
            Assert.AreEqual((byte)ServiceRequestCode.READ_SYS_MEM, read[42]);
            Assert.AreEqual((byte)SegmentSelector.BIT_Q, read[43]);
            blockedRead.TrySetResult(true);
            Wait(releaseRead);
            Respond(socket);
            ExpectShortWrite(socket, SegmentSelector.BIT_Q, new byte[] { 0x05 });
            ExpectShortWrite(socket, SegmentSelector.BIT_I, new byte[] { 0x8a });
            ExpectShortWrite(socket, SegmentSelector.WORD_AQ, new byte[] { 0x34, 0x12, 0xcd, 0xab });
            ExpectShortWrite(socket, SegmentSelector.WORD_AI, new byte[] { 0x89, 0x67, 0x67, 0x45 });
            ExpectShortWrite(socket, SegmentSelector.WORD_R, new byte[] { 1, 2, 3, 4 });
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        var di = new[] { true, false, true, false, false, false, false, false };
        var output = new[] { false, true, false, true, false, false, false, true };
        var gi = new ushort[] { 0x1234, 0xabcd };
        var go = new ushort[] { 0x6789, 0x4567 };
        var snpx = new byte[] { 1, 2, 3, 4 };
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            var read = robot.AsyncReadDI(1, 1);
            await Bounded(blockedRead.Task);
            var writes = new[]
            {
                robot.AsyncWriteDI(1, di), robot.AsyncWriteDO(1, output),
                robot.AsyncWriteGI(1, gi), robot.AsyncWriteGO(1, go), robot.AsyncWriteSNPX(1, snpx)
            };
            foreach (var write in writes)
                Assert.IsFalse(write.IsCompleted);
            Array.Clear(di, 0, di.Length);
            Array.Clear(output, 0, output.Length);
            Array.Clear(gi, 0, gi.Length);
            Array.Clear(go, 0, go.Length);
            Array.Clear(snpx, 0, snpx.Length);
            releaseRead.Set();
            await Bounded(read);
            await Bounded(Task.WhenAll(writes));
        }
        finally
        {
            releaseRead.Set();
            await Bounded(Task.Run(robot.Disconnect));
            releaseRead.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task FailedWriteFaultsAndIsNotReplayedAfterReconnect()
    {
        var poll = Signal();
        var releasePoll = new ManualResetEventSlim();
        var reconnectedPoll = Signal();
        using var server = new Controller(socket =>
        {
            InitializeRegister(socket);
            ExpectPoll(socket);
            poll.TrySetResult(true);
            Wait(releasePoll);
            Respond(socket);
            while (true)
            {
                var request = ReadRequest(socket);
                if (IsRegisterWrite(request))
                {
                    Respond(socket, false);
                    break;
                }
                AssertPoll(request);
                Respond(socket);
            }
            ServePollsUntilClosed(socket);
        }, socket =>
        {
            InitializeRegister(socket);
            for (var i = 0; i < 3; i++)
            {
                var request = ReadRequest(socket);
                AssertPoll(request);
                if (i == 2)
                {
                    reconnectedPoll.TrySetResult(true);
                    ExpectClosed(socket);
                }
                else
                    Respond(socket);
            }
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = robot.R[1];
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(poll.Task);
            var pending = holder.WriteAsync(42f);
            releasePoll.Set();
            await ExpectFault(pending);
            await Bounded(Task.Run(robot.Disconnect));
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(reconnectedPoll.Task);
        }
        finally
        {
            releasePoll.Set();
            await Bounded(Task.Run(robot.Disconnect));
            releasePoll.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task QueuedWriteIsCanceledOnDisconnectAndNotReplayed()
    {
        var poll = Signal();
        var reconnectedPoll = Signal();
        using var server = new Controller(socket =>
        {
            InitializeRegister(socket);
            ExpectPoll(socket);
            poll.TrySetResult(true);
            ExpectClosed(socket);
        }, socket =>
        {
            InitializeRegister(socket);
            ObservePollsWithoutWrites(socket, reconnectedPoll);
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = robot.R[1];
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(poll.Task);
            var pending = holder.WriteAsync(42f);
            Assert.IsFalse(pending.IsCompleted);
            await Bounded(Task.Run(robot.Disconnect));
            await ExpectUnsuccessful(pending);
            Assert.IsTrue(pending.IsCanceled, "A queued write interrupted by disconnect should be canceled.");
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(reconnectedPoll.Task);
        }
        finally
        {
            await Bounded(Task.Run(robot.Disconnect));
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task DisconnectInterruptsBlockedPoll()
    {
        var poll = Signal();
        using var server = new Controller(socket =>
        {
            InitializeRegister(socket);
            ExpectPoll(socket);
            poll.TrySetResult(true);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        _ = robot.R[1];
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, 30000));
            await Bounded(poll.Task);
            await Bounded(Task.Run(robot.Disconnect));
            Assert.IsFalse(robot.IsConnected);
        }
        finally
        {
            await Bounded(Task.Run(robot.Disconnect));
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task WatcherCallbackCanDisconnectAndAsyncReconnectWaitsForExit()
    {
        var reconnect = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncError = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = Signal();
        var nextPoll = Signal();
        var releaseWatcher = new ManualResetEventSlim();
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG");
            var read = ReadRequest(socket);
            Assert.AreEqual((byte)ServiceRequestCode.READ_SYS_MEM, read[42]);
            Assert.AreEqual((byte)SegmentSelector.BIT_Q, read[43]);
            Respond(socket, firstByte: 1);
            ExpectClosed(socket);
        }, socket =>
        {
            accepted.TrySetResult(true);
            Handshake(socket);
            Command(socket, "CLRASG");
            var read = ReadRequest(socket);
            Assert.AreEqual((byte)ServiceRequestCode.READ_SYS_MEM, read[42]);
            Assert.AreEqual((byte)SegmentSelector.BIT_Q, read[43]);
            nextPoll.TrySetResult(true);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = robot.SDI[1];
        var called = 0;
        holder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(holder.Value) || Interlocked.Exchange(ref called, 1) != 0)
                return;
            try
            {
                robot.Disconnect();
                try
                {
                    robot.Connect("127.0.0.1", server.Port, TimeoutMs);
                    syncError.TrySetResult(null);
                }
                catch (Exception ex) { syncError.TrySetResult(ex); }
                reconnect.TrySetResult(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
                Wait(releaseWatcher);
            }
            catch (Exception ex)
            {
                syncError.TrySetException(ex);
                reconnect.TrySetException(ex);
            }
        };
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            var reconnecting = await Bounded(reconnect.Task);
            Assert.IsInstanceOfType<InvalidOperationException>(await Bounded(syncError.Task));
            Assert.IsFalse(reconnecting.IsCompleted);
            Assert.AreNotSame(accepted.Task, await Task.WhenAny(accepted.Task, Task.Delay(200)),
                "A new TCP session started while the old watcher was still in its callback.");
            releaseWatcher.Set();
            await Bounded(reconnecting);
            await Bounded(nextPoll.Task);
        }
        finally
        {
            releaseWatcher.Set();
            await Bounded(Task.Run(robot.Disconnect));
            releaseWatcher.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task AssignmentAllocationRejectsRangeOverflow()
    {
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG");
            Command(socket, "SETASG 1 65536 R[1] 0");
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await ExpectFault(robot.AsyncWriteAssignment(0, "R[1] 0"));
            await ExpectFault(robot.AsyncWriteAssignment(-1, "R[1] 0"));
            Assert.AreEqual(1, await Bounded(robot.AsyncWriteAssignment(65536, "R[1] 0")));
            await ExpectFault(robot.AsyncWriteAssignment(1, "R[2] 0"));
        }
        finally
        {
            await Bounded(Task.Run(robot.Disconnect));
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task HolderRegisteredAfterConnectGetsAssignedBeforeUse()
    {
        var blockedRead = Signal();
        var pollAfterWrite = Signal();
        var releaseRead = new ManualResetEventSlim();
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG");
            var read = ReadRequest(socket);
            Assert.AreEqual((byte)ServiceRequestCode.READ_SYS_MEM, read[42]);
            Assert.AreEqual((byte)SegmentSelector.BIT_Q, read[43]);
            blockedRead.TrySetResult(true);
            Wait(releaseRead);
            Respond(socket);
            Command(socket, "SETASG 1 2 R[1] 0");
            while (true)
            {
                var request = ReadRequest(socket);
                if (IsRegisterWrite(request))
                {
                    CollectionAssert.AreEqual(BitConverter.GetBytes(42f), request.Skip(48).Take(4).ToArray());
                    Respond(socket);
                    break;
                }
                AssertPoll(request);
                Respond(socket);
            }
            ExpectPoll(socket);
            pollAfterWrite.TrySetResult(true);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            var read = robot.AsyncReadDI(1, 1);
            await Bounded(blockedRead.Task);
            var holder = robot.R[1];
            var write = holder.WriteAsync(42f);
            releaseRead.Set();
            await Bounded(read);
            await Bounded(write);
            await Bounded(pollAfterWrite.Task);
        }
        finally
        {
            releaseRead.Set();
            await Bounded(Task.Run(robot.Disconnect));
            releaseRead.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task DynamicAssignmentFailureIsReportedAndNotReplayed()
    {
        var blockedRead = Signal();
        var firstClosed = Signal();
        var releaseRead = new ManualResetEventSlim();
        var reconnectedPoll = Signal();
        using var server = new Controller(socket =>
        {
            Handshake(socket);
            Command(socket, "CLRASG");
            var read = ReadRequest(socket);
            Assert.AreEqual((byte)ServiceRequestCode.READ_SYS_MEM, read[42]);
            Assert.AreEqual((byte)SegmentSelector.BIT_Q, read[43]);
            blockedRead.TrySetResult(true);
            Wait(releaseRead);
            Respond(socket);
            try
            {
                Command(socket, "SETASG 1 2 R[1] 0", false);
                ExpectClosed(socket);
                firstClosed.TrySetResult(true);
            }
            catch (Exception ex)
            {
                firstClosed.TrySetException(ex);
                throw;
            }
        }, socket =>
        {
            InitializeRegister(socket);
            ObservePollsWithoutWrites(socket, reconnectedPoll, includeDigitalInput: true);
        });
        var robot = new ExRobotIF(new RobotIF());
        _ = robot.SDI[1];
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(blockedRead.Task);
            var holder = robot.R[1];
            var pending = holder.WriteAsync(42f);
            releaseRead.Set();
            await ExpectUnsuccessful(pending);
            await Until(() => !robot.IsConnected && robot.LastError != null);
            StringAssert.Contains(robot.LastError.ToString(), "Write");
            await Bounded(firstClosed.Task);
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(reconnectedPoll.Task);
        }
        finally
        {
            releaseRead.Set();
            await Bounded(Task.Run(robot.Disconnect));
            releaseRead.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task PollFailureIsObservableAndNotifiesDisconnection()
    {
        var poll = Signal();
        var release = new ManualResetEventSlim();
        using var server = new Controller(socket =>
        {
            InitializeRegister(socket);
            ExpectPoll(socket);
            poll.TrySetResult(true);
            Wait(release);
            socket.Shutdown(SocketShutdown.Send);
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        IExRobotIF api = robot;
        _ = robot.R[1];
        var notifications = new ConcurrentQueue<bool>();
        var errors = new ConcurrentQueue<Exception?>();
        robot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExRobotIF.IsConnected))
                notifications.Enqueue(robot.IsConnected);
            if (e.PropertyName == nameof(ExRobotIF.LastError))
                errors.Enqueue(api.LastError);
        };
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs));
            await Bounded(poll.Task);
            release.Set();
            await Until(() => !api.IsConnected && api.LastError != null && notifications.Contains(false) && !errors.IsEmpty);
            Assert.IsNotNull(api.LastError);
            Assert.IsTrue(ContainsEndOfStream(api.LastError), $"Original polling error was lost: {api.LastError}");
            Assert.IsTrue(errors.Any(error => error != null && ContainsEndOfStream(error)));
            Assert.IsTrue(notifications.Contains(true));
        }
        finally
        {
            release.Set();
            await Bounded(Task.Run(robot.Disconnect));
            release.Dispose();
        }
        await server.Complete();
    }

    [TestMethod]
#pragma warning disable MSTEST0045
    [Timeout(15000)]
#pragma warning restore MSTEST0045
    public async Task ConcurrentConnectsDoNotCreateMultipleSessions()
    {
        var entered = Signal();
        var release = new ManualResetEventSlim();
        using var server = new Controller(socket =>
        {
            var init = ReadRequest(socket);
            Assert.AreEqual((byte)PacketType.INIT, init[0]);
            entered.TrySetResult(true);
            Wait(release);
            Respond(socket);
            Assert.AreEqual((byte)PacketType.UNKNOWN_8, ReadRequest(socket)[0]);
            Respond(socket);
            Command(socket, "CLRASG");
            ExpectClosed(socket);
        });
        var robot = new ExRobotIF(new RobotIF());
        try
        {
            var first = robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs);
            await Bounded(entered.Task);
            var second = robot.ConnectAsync("127.0.0.1", server.Port, TimeoutMs);
            release.Set();
            await Bounded(first);
            try { await Bounded(second); }
            catch (Exception ex) when (ex is not TimeoutException) { }
            Assert.IsTrue(robot.IsConnected);
            Assert.IsFalse(server.HasPendingConnection, "A second TCP session was opened.");
        }
        finally
        {
            release.Set();
            await Bounded(Task.Run(robot.Disconnect));
            release.Dispose();
        }
        await server.Complete();
    }

    private static void Session(Socket socket, TaskCompletionSource<bool> poll)
    {
        InitializeRegister(socket);
        ExpectPoll(socket);
        poll.TrySetResult(true);
        ExpectClosed(socket);
    }

    private static void InitializeRegister(Socket socket)
    {
        Handshake(socket);
        Command(socket, "CLRASG");
        Command(socket, "SETASG 1 2 R[1] 0");
    }

    private static void Handshake(Socket socket)
    {
        Assert.AreEqual((byte)PacketType.INIT, ReadRequest(socket)[0]);
        Respond(socket);
        Assert.AreEqual((byte)PacketType.UNKNOWN_8, ReadRequest(socket)[0]);
        Respond(socket);
    }

    private static void Command(Socket socket, string command, bool success = true)
    {
        AssertCommand(ReadRequest(socket), command);
        Respond(socket, success);
    }

    private static void AssertCommand(byte[] request, string command)
    {
        var serviceOffset = request.Length > PacketBase.HEADER_LENGTH ? 50 : 42;
        Assert.AreEqual((byte)ServiceRequestCode.WRITE_SYS_MEM, request[serviceOffset]);
        Assert.AreEqual((byte)SegmentSelector.BYTE_G, request[serviceOffset + 1]);
        var payload = request.Length > PacketBase.HEADER_LENGTH ? request.Skip(PacketBase.HEADER_LENGTH) : request.Skip(48).Take(6);
        StringAssert.StartsWith(Encoding.ASCII.GetString(payload.ToArray()), command);
    }

    private static bool IsRegisterWrite(byte[] request) =>
        request[42] == (byte)ServiceRequestCode.WRITE_SYS_MEM && request[43] == (byte)SegmentSelector.WORD_R;

    private static void ExpectShortWrite(Socket socket, SegmentSelector selector, byte[] payload)
    {
        var request = ReadRequest(socket);
        Assert.AreEqual((byte)ServiceRequestCode.WRITE_SYS_MEM, request[42]);
        Assert.AreEqual((byte)selector, request[43]);
        CollectionAssert.AreEqual(payload, request.Skip(48).Take(payload.Length).ToArray());
        Respond(socket);
    }

    private static void ExpectRegisterWrite(Socket socket, float value)
    {
        while (true)
        {
            var request = ReadRequest(socket);
            if (IsRegisterWrite(request))
            {
                CollectionAssert.AreEqual(BitConverter.GetBytes(value), request.Skip(48).Take(4).ToArray());
                return;
            }
            AssertPoll(request);
            Respond(socket);
        }
    }

    private static void AssertPoll(byte[] request)
    {
        Assert.AreEqual((byte)ServiceRequestCode.READ_SYS_MEM, request[42], "Unexpected write or request during polling.");
        Assert.AreEqual((byte)SegmentSelector.WORD_R, request[43]);
        Assert.AreEqual(0, request[44] | request[45] << 8, "Poll used a stale assignment offset.");
    }

    private static void ExpectPoll(Socket socket) => AssertPoll(ReadRequest(socket));

    private static void ObservePollsWithoutWrites(Socket socket, TaskCompletionSource<bool> poll, bool includeDigitalInput = false)
    {
        for (var i = 0; i < 3;)
        {
            var request = ReadRequest(socket);
            if (includeDigitalInput && request[42] == (byte)ServiceRequestCode.READ_SYS_MEM &&
                request[43] == (byte)SegmentSelector.BIT_Q)
            {
                Respond(socket);
                continue;
            }
            AssertPoll(request);
            if (++i == 3)
            {
                poll.TrySetResult(true);
                ExpectClosed(socket);
            }
            else
                Respond(socket);
        }
    }

    private static void ServePollsUntilClosed(Socket socket)
    {
        while (socket.Receive(new byte[1], 0, 1, SocketFlags.Peek) != 0)
        {
            AssertPoll(ReadRequest(socket));
            Respond(socket);
        }
    }

    private static byte[] ReadRequest(Socket socket)
    {
        var header = ReadExactly(socket, PacketBase.HEADER_LENGTH);
        return header.Concat(ReadExactly(socket, PacketBase.ExtraLengthFromHeaderBuffer(header))).ToArray();
    }

    private static byte[] ReadExactly(Socket socket, int count)
    {
        var bytes = new byte[count];
        for (var offset = 0; offset < count;)
        {
            var received = socket.Receive(bytes, offset, count - offset, SocketFlags.None);
            if (received == 0) throw new EndOfStreamException("Client closed during a request.");
            offset += received;
        }
        return bytes;
    }

    private static void Respond(Socket socket, bool success = true, byte firstByte = 0)
    {
        var response = new byte[PacketBase.HEADER_LENGTH];
        response[31] = (byte)(success ? MessageType.SHORT_ACK : MessageType.SHORT_FAILED);
        response[44] = firstByte;
        for (var offset = 0; offset < response.Length;)
        {
            var sent = socket.Send(response, offset, response.Length - offset, SocketFlags.None);
            if (sent == 0) throw new EndOfStreamException("Client closed during a response.");
            offset += sent;
        }
    }

    private static void RespondPosition(Socket socket)
    {
        var response = new byte[PacketBase.HEADER_LENGTH + 100];
        response[4] = 100;
        response[31] = (byte)MessageType.LONG_ACK;
        for (var offset = 0; offset < response.Length;)
        {
            var sent = socket.Send(response, offset, response.Length - offset, SocketFlags.None);
            if (sent == 0) throw new EndOfStreamException("Client closed during a response.");
            offset += sent;
        }
    }

    private static void ExpectClosed(Socket socket) => Assert.AreEqual(0, socket.Receive(new byte[1]));
    private static void Wait(ManualResetEventSlim signal) => Assert.IsTrue(signal.Wait(TimeoutMs), "Test synchronization timed out.");
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Bounded(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeoutMs)) != task)
            throw new TimeoutException("Operation did not finish in time.");
        await task;
    }

    private static async Task<T> Bounded<T>(Task<T> task)
    {
        await Bounded((Task)task);
        return await task;
    }

    private static bool ContainsEndOfStream(Exception error)
    {
        if (error is EndOfStreamException) return true;
        if (error is AggregateException aggregate)
            return aggregate.InnerExceptions.Any(ContainsEndOfStream);
        return error.InnerException != null && ContainsEndOfStream(error.InnerException);
    }

    private static async Task ExpectFault(Task task)
    {
        try { await Bounded(task); }
        catch (Exception ex) when (ex is not TimeoutException && ex is not OperationCanceledException) { return; }
        Assert.Fail("Expected the operation to fault.");
    }

    private static async Task ExpectUnsuccessful(Task task)
    {
        try { await Bounded(task); }
        catch (Exception ex) when (ex is not TimeoutException) { return; }
        Assert.Fail("Expected the operation to fail or be canceled.");
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Expected state was not observed.");
            await Task.Delay(10);
        }
    }

    private sealed class Controller : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _task;
        private Socket? _socket;
        public ushort Port { get; }
        public bool HasPendingConnection => _listener.Pending();

        public Controller(params Action<Socket>[] sessions)
        {
            _listener.Start();
            Port = (ushort)((IPEndPoint)_listener.LocalEndpoint).Port;
            _task = Task.Run(() =>
            {
                foreach (var session in sessions)
                {
                    using var socket = _listener.AcceptSocket();
                    _socket = socket;
                    socket.NoDelay = true;
                    socket.ReceiveTimeout = TimeoutMs * 2;
                    socket.SendTimeout = TimeoutMs * 2;
                    session(socket);
                    _socket = null;
                }
            });
        }

        public Task Complete() => Bounded(_task);
        public void Dispose()
        {
            _listener.Stop();
            _socket?.Dispose();
        }
    }
}
