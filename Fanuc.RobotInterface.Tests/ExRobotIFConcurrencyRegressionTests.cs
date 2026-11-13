using System.IO;
using System.Net;
using System.Net.Sockets;
using Fanuc.RobotInterface;
using Fanuc.SRTP;

#pragma warning disable MSTEST0045 // Bound synchronous socket operations that cannot cooperate with cancellation.

namespace Fanuc.RobotInterface.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ExRobotIFConcurrencyRegressionTests
{
    private const int TimeoutMs = 5000;

    [TestMethod]
    public void HolderPlumbingIsNotPublic()
    {
        Assert.IsNull(typeof(ExRobotIF).GetNestedType("IRobotValueHolder"));
        Assert.IsNull(typeof(ExRobotIF).GetNestedType("IRobotComplexDataHolder"));
        Assert.IsNull(typeof(ExRobotIF.RobotDataHolderBase<bool>).GetMethod("PullValue"));
        Assert.IsNull(typeof(ExRobotIF.RobotWritableComplexDataHolderBase<bool>).GetMethod("EnsureAssignment"));
        Assert.IsNull(typeof(ExRobotIF.RobotWritableComplexDataHolderBase<bool>).GetMethod("WriteAssignment"));
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task DisconnectRevokesReconnectRequestedDuringWatcherShutdown()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        using var callbackRelease = new ManualResetEventSlim();
        var callbackEntered = Signal();
        Task? pending = null;
        var server = Task.Run(() =>
        {
            using var socket = Accept(listener);
            Initialize(socket);
            ReadRequest(socket);
            Respond(socket, 1);
            Assert.AreEqual(0, socket.Receive(new byte[1]));
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = robot.SDI[1];
        holder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "Value") return;
            robot.Disconnect();
            pending = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            robot.Disconnect();
            callbackEntered.TrySetResult(true);
            Assert.IsTrue(callbackRelease.Wait(TimeoutMs));
        };
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
            await Bounded(callbackEntered.Task);
            callbackRelease.Set();
            Assert.IsNotNull(pending);
            try
            {
                await Bounded(pending!);
                Assert.Fail("A canceled reconnect completed successfully.");
            }
            catch (OperationCanceledException) { }
            await Bounded(server);
            Assert.IsFalse(listener.Pending(), "Canceled reconnect opened another socket.");
            Assert.IsFalse(robot.IsConnected);
        }
        finally
        {
            callbackRelease.Set();
            robot.Disconnect();
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task BackendConnectCallbackCannotIssueCommandsBeforeInitialization()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        var callback = Signal();
        var polled = Signal();
        Exception? rejected = null;
        Exception? assignmentRejected = null;
        Exception? connectionRejected = null;
        var backend = new RobotIF();
        var robot = new ExRobotIF(backend);
        _ = robot.R[1];
        backend.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RobotIF.IsConnected) || !backend.IsConnected) return;
            try { robot.WriteCommand("EARLY"); }
            catch (Exception ex) { rejected = ex; }
            try { robot.AsyncWriteAssignment(2, "R[2] 0").GetAwaiter().GetResult(); }
            catch (Exception ex) { assignmentRejected = ex; }
            try { robot.ConnectAsync("127.0.0.1", port, TimeoutMs).GetAwaiter().GetResult(); }
            catch (Exception ex) { connectionRejected = ex; }
            callback.TrySetResult(true);
        };
        var server = Task.Run(() =>
        {
            using var socket = Accept(listener);
            Handshake(socket);
            AssertCommand(ReadRequest(socket), "CLRASG");
            Respond(socket);
            AssertCommand(ReadRequest(socket), "SETASG 1 2 R[1] 0");
            Respond(socket);
            ReadRequest(socket);
            polled.TrySetResult(true);
            Assert.AreEqual(0, socket.Receive(new byte[1]));
        });
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
            await Bounded(callback.Task);
            await Bounded(polled.Task);
            Assert.IsInstanceOfType<IOException>(rejected);
            Assert.IsInstanceOfType<IOException>(assignmentRejected);
            Assert.IsInstanceOfType<InvalidOperationException>(connectionRejected);
        }
        finally { robot.Disconnect(); }
        await Bounded(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task SchedulerDoesNotInlineAnotherSchedulersQueuedTask()
    {
        var type = typeof(ExRobotIF).Assembly.GetType("System.Threading.Tasks.Schedulers.LimitedConcurrencyLevelTaskScheduler", true)!;
        var a = (TaskScheduler)Activator.CreateInstance(type, 1)!;
        var b = (TaskScheduler)Activator.CreateInstance(type, 1)!;
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var ran = Signal();
        var blocker = Task.Factory.StartNew(() =>
        {
            entered.TrySetResult(true);
            Assert.IsTrue(release.Wait(TimeoutMs));
        }, CancellationToken.None, TaskCreationOptions.None, b);
        await Bounded(entered.Task);
        var queued = Task.Factory.StartNew(() => ran.TrySetResult(true), CancellationToken.None, TaskCreationOptions.None, b);
        var inline = type.GetMethod("TryExecuteTaskInline",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var waiter = Task.Factory.StartNew(() => (bool)inline.Invoke(b, new object[] { queued, true })!,
            CancellationToken.None, TaskCreationOptions.None, a);
        try
        {
            await Bounded(waiter);
            Assert.IsFalse(await waiter, "B's task ran inline while B was occupied.");
            Assert.IsFalse(ran.Task.IsCompleted);
        }
        finally { release.Set(); }
        await Bounded(Task.WhenAll(blocker, waiter));
        await Bounded(ran.Task);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task SuccessfulReconnectNotifiesErrorReset()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        var server = Task.Run(() =>
        {
            using (var socket = Accept(listener))
            {
                Handshake(socket);
                AssertCommand(ReadRequest(socket), "CLRASG");
                Respond(socket, failure: true);
                Assert.AreEqual(0, socket.Receive(new byte[1]));
            }
            using (var socket = Accept(listener))
            {
                Initialize(socket);
                Assert.AreEqual(0, socket.Receive(new byte[1]));
            }
        });
        var robot = new ExRobotIF(new RobotIF());
        var reset = Signal();
        robot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExRobotIF.LastError) && robot.LastError == null)
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => robot.Connect("127.0.0.1", port, TimeoutMs));
                Assert.IsFalse(robot.ConnectAsync("127.0.0.1", port, TimeoutMs).IsCompleted);
                reset.TrySetResult(true);
            }
        };
        try
        {
            try
            {
                await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
                Assert.Fail("Expected initialization failure.");
            }
            catch (IOException) { }
            Assert.IsNotNull(robot.LastError);
            await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
            Assert.IsTrue(reset.Task.IsCompleted, "Error reset was not notified before connect completed.");
            Assert.IsNull(robot.LastError);
        }
        finally { robot.Disconnect(); }
        await Bounded(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ConnectSubscriberFailureClosesSessionAndAllowsRetry()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        var server = Task.Run(() =>
        {
            for (var i = 0; i < 2; i++)
            {
                using var socket = Accept(listener);
                Initialize(socket);
                Assert.AreEqual(0, socket.Receive(new byte[1]));
            }
        });
        var robot = new ExRobotIF(new RobotIF());
        var error = new InvalidOperationException("Subscriber failed.");
        var called = 0;
        robot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExRobotIF.IsConnected) && robot.IsConnected &&
                Interlocked.Increment(ref called) == 1)
                throw error;
        };
        try
        {
            try
            {
                await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
                Assert.Fail("Expected subscriber failure.");
            }
            catch (InvalidOperationException ex) { Assert.AreSame(error, ex); }
            Assert.IsFalse(robot.IsConnected);
            Assert.AreSame(error, robot.LastError);
            await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
            Assert.IsTrue(robot.IsConnected);
        }
        finally { robot.Disconnect(); }
        await Bounded(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ConcurrentConnectAwaitsSuccessNotificationsAndSharesFailure()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var server = Task.Run(() =>
        {
            using var socket = Accept(listener);
            Initialize(socket);
            Assert.AreEqual(0, socket.Receive(new byte[1]));
        });
        var robot = new ExRobotIF(new RobotIF());
        var error = new InvalidOperationException("Notification failure.");
        robot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ExRobotIF.IsConnected) || !robot.IsConnected) return;
            Assert.ThrowsExactly<InvalidOperationException>(() => robot.Connect("127.0.0.1", port, TimeoutMs));
            Assert.IsFalse(robot.ConnectAsync("127.0.0.1", port, TimeoutMs).IsCompleted);
            entered.TrySetResult(true);
            Assert.IsTrue(release.Wait(TimeoutMs));
            throw error;
        };
        try
        {
            var first = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            await Bounded(entered.Task);
            var second = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            Assert.IsFalse(second.IsCompleted);
            release.Set();
            foreach (var task in new[] { first, second })
            {
                try { await Bounded(task); Assert.Fail("Expected subscriber failure."); }
                catch (InvalidOperationException ex) { Assert.AreSame(error, ex); }
            }
            Assert.IsFalse(robot.IsConnected);
            Assert.AreSame(error, robot.LastError);
        }
        finally { release.Set(); robot.Disconnect(); }
        await Bounded(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task WatcherConnectAsyncObservesPendingNotificationFailure()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        using var successEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var watcherEntered = Signal();
        Task? watcherConnect = null;
        var server = Task.Run(() =>
        {
            using var socket = Accept(listener);
            Initialize(socket);
            while (socket.Receive(new byte[1], 0, 1, SocketFlags.Peek) != 0)
            {
                ReadRequest(socket);
                Respond(socket, 1);
            }
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = robot.SDI[1];
        var error = new InvalidOperationException("Success notification failed.");
        robot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ExRobotIF.IsConnected) || !robot.IsConnected) return;
            successEntered.Set();
            Assert.IsTrue(release.Wait(TimeoutMs));
            throw error;
        };
        holder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "Value") return;
            Assert.IsTrue(successEntered.Wait(TimeoutMs));
            Assert.ThrowsExactly<InvalidOperationException>(() => robot.Connect("127.0.0.1", port, TimeoutMs));
            watcherConnect = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            watcherEntered.TrySetResult(true);
        };
        try
        {
            var first = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            await Bounded(watcherEntered.Task);
            Assert.IsNotNull(watcherConnect);
            Assert.IsFalse(watcherConnect!.IsCompleted);
            release.Set();
            foreach (var task in new[] { first, watcherConnect! })
            {
                try { await Bounded(task); Assert.Fail("Expected subscriber failure."); }
                catch (InvalidOperationException ex) { Assert.AreSame(error, ex); }
            }
            Assert.IsFalse(robot.IsConnected);
        }
        finally { release.Set(); robot.Disconnect(); }
        await Bounded(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task FailureNotificationsFinishBeforeReconnectAndAllowReentrantCalls()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var connected = Signal();
        Task? reentrantConnect = null;
        Exception? observed = null;
        var server = Task.Run(() =>
        {
            using (var socket = Accept(listener))
            {
                Handshake(socket);
                AssertCommand(ReadRequest(socket), "CLRASG");
                Respond(socket, failure: true);
                Assert.AreEqual(0, socket.Receive(new byte[1]));
            }
            using (var socket = Accept(listener))
            {
                connected.TrySetResult(true);
                Initialize(socket);
                Assert.AreEqual(0, socket.Receive(new byte[1]));
            }
        });
        var robot = new ExRobotIF(new RobotIF());
        robot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ExRobotIF.LastError) || robot.LastError == null) return;
            observed = robot.LastError;
            Assert.IsFalse(robot.IsConnected);
            robot.Disconnect();
            Assert.ThrowsExactly<InvalidOperationException>(() => robot.Connect("127.0.0.1", port, TimeoutMs));
            reentrantConnect = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            entered.TrySetResult(true);
            Assert.IsTrue(release.Wait(TimeoutMs));
        };
        try
        {
            var failed = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            await Bounded(entered.Task);
            var concurrent = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            Assert.AreSame(observed, robot.LastError);
            Assert.IsFalse(connected.Task.IsCompleted);
            Assert.IsFalse(concurrent.IsCompleted);
            release.Set();
            try { await Bounded(failed); Assert.Fail("Expected initialization failure."); }
            catch (IOException) { }
            Assert.IsNotNull(reentrantConnect);
            await Bounded(Task.WhenAll(reentrantConnect!, concurrent));
            Assert.IsTrue(robot.IsConnected);
        }
        finally { release.Set(); robot.Disconnect(); }
        await Bounded(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ExternalAssignmentMutationCannotCrossReconnect()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var closed = Signal();
        var reconnected = Signal();
        var server = Task.Run(() =>
        {
            using (var socket = Accept(listener))
            {
                Initialize(socket);
                AssertCommand(ReadRequest(socket), "SETASG 1 2 R[1] 0");
                Respond(socket);
                Assert.AreEqual(0, socket.Receive(new byte[1]));
                closed.TrySetResult(true);
            }
            using (var socket = Accept(listener))
            {
                reconnected.TrySetResult(true);
                Initialize(socket);
                AssertCommand(ReadRequest(socket), "SETASG 1 2 R[9] 0");
                Respond(socket);
                AssertCommand(ReadRequest(socket), "SETASG 3 2 R[1] 0");
                Respond(socket);
                Assert.AreEqual(0, socket.Receive(new byte[1]));
            }
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = new DelayedAssignmentHolder(robot, entered, release);
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
            var assignment = holder.WriteAsync(false);
            await Bounded(entered.Task);
            Assert.AreNotSame(TaskScheduler.Default, holder.AssignmentScheduler,
                "The holder mutated its assignment outside the session scheduler.");
            var disconnect = Task.Run(robot.Disconnect);
            await Bounded(closed.Task);
            var reconnect = robot.ConnectAsync("127.0.0.1", port, TimeoutMs);
            Assert.IsFalse(reconnected.Task.IsCompleted);
            release.Set();
            await Bounded(assignment);
            await Bounded(disconnect);
            await Bounded(reconnect);
            await Bounded(robot.AsyncWriteAssignment(2, "R[9] 0"));
            await Bounded(holder.WriteAsync(false));
            Assert.AreEqual(3, holder.CurrentOffset);
        }
        finally { release.Set(); robot.Disconnect(); }
        await Bounded(server);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task WritePreparationCannotCrossReconnect()
    {
        var listener = Listen(out var port);
        using var cleanup = new ListenerCleanup(listener);
        using var release = new ManualResetEventSlim();
        var preparing = Signal();
        var server = Task.Run(() =>
        {
            for (var i = 0; i < 2; i++)
            {
                using var socket = Accept(listener);
                Initialize(socket);
                Assert.AreEqual(0, socket.Receive(new byte[1]), "An old write reached the new session.");
            }
        });
        var robot = new ExRobotIF(new RobotIF());
        var holder = new PreparingHolder(robot, preparing, release);
        try
        {
            await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
            var write = Task.Run(() => holder.WriteAsync(true));
            await Bounded(preparing.Task);
            await Bounded(Task.Run(robot.Disconnect));
            await Bounded(robot.ConnectAsync("127.0.0.1", port, TimeoutMs));
            release.Set();
            try { await Bounded(write); Assert.Fail("Expected stale write to fail."); }
            catch (IOException) { }
            Assert.IsTrue(robot.IsConnected);
        }
        finally { release.Set(); robot.Disconnect(); }
        await Bounded(server);
    }

    private sealed class PreparingHolder : ExRobotIF.RobotWritableDataHolderBase<bool>
    {
        private readonly TaskCompletionSource<bool> _entered;
        private readonly ManualResetEventSlim _release;
        public PreparingHolder(ExRobotIF robot, TaskCompletionSource<bool> entered, ManualResetEventSlim release)
            : base(robot) { _entered = entered; _release = release; }
        protected override Action PrepareWrite(bool value)
        {
            _entered.TrySetResult(true);
            Assert.IsTrue(_release.Wait(TimeoutMs));
            return () => Robot.WriteDO(1, value);
        }
        protected override bool ReadValue() => false;
        protected override void WriteValue(bool value) => Robot.WriteDO(1, value);
    }

    private sealed class DelayedAssignmentHolder : ExRobotIF.RobotWritableComplexDataHolderBase<bool>
    {
        private readonly TaskCompletionSource<bool> _entered;
        private readonly ManualResetEventSlim _release;
        private int _assignments;
        public DelayedAssignmentHolder(ExRobotIF robot, TaskCompletionSource<bool> entered, ManualResetEventSlim release)
            : base(robot) { _entered = entered; _release = release; }
        public int CurrentOffset => Offset;
        public TaskScheduler? AssignmentScheduler { get; private set; }
        protected override void WriteAssignment()
        {
            AssignmentScheduler = TaskScheduler.Current;
            var offset = Robot.AsyncWriteAssignment(2, "R[1] 0").GetAwaiter().GetResult();
            if (Interlocked.Increment(ref _assignments) == 1)
            {
                _entered.TrySetResult(true);
                Assert.IsTrue(_release.Wait(TimeoutMs));
            }
            Offset = offset;
        }
        protected override bool ReadValue() => false;
        protected override void WriteValue(bool value) { }
    }

    private static TcpListener Listen(out ushort port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }
    private sealed class ListenerCleanup : IDisposable
    {
        private readonly TcpListener _listener;
        public ListenerCleanup(TcpListener listener) => _listener = listener;
        public void Dispose() => _listener.Stop();
    }
    private static Socket Accept(TcpListener listener)
    {
        var socket = listener.AcceptSocket();
        socket.ReceiveTimeout = TimeoutMs;
        socket.SendTimeout = TimeoutMs;
        return socket;
    }
    private static void Handshake(Socket socket)
    {
        ReadRequest(socket);
        Respond(socket);
        ReadRequest(socket);
        Respond(socket);
    }
    private static void Initialize(Socket socket)
    {
        Handshake(socket);
        AssertCommand(ReadRequest(socket), "CLRASG");
        Respond(socket);
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
            var read = socket.Receive(bytes, offset, count - offset, SocketFlags.None);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }
    private static void Respond(Socket socket, byte value = 0, bool failure = false)
    {
        var response = new byte[PacketBase.HEADER_LENGTH];
        response[31] = (byte)(failure ? MessageType.SHORT_FAILED : MessageType.SHORT_ACK);
        response[44] = value;
        for (var offset = 0; offset < response.Length;)
            offset += socket.Send(response, offset, response.Length - offset, SocketFlags.None);
    }
    private static void AssertCommand(byte[] request, string command)
    {
        var payload = request.Length > PacketBase.HEADER_LENGTH ? request.Skip(PacketBase.HEADER_LENGTH) : request.Skip(48).Take(6);
        StringAssert.StartsWith(System.Text.Encoding.ASCII.GetString(payload.ToArray()), command);
    }
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Bounded(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeoutMs)) != task) throw new TimeoutException();
        await task;
    }
}
