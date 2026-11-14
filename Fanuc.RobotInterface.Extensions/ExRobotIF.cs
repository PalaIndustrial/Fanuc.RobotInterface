using Fanuc.RobotInterface.Collections;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Schedulers;

namespace Fanuc.RobotInterface
{
    public partial class ExRobotIF : IExRobotIF, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly RobotIF _Robot;

        private readonly object _LifecycleLock = new object();
        private Session _Session;
        private Exception _LastError;
        private long _ConnectVersion;
        [ThreadStatic] private static CallbackScope _CallbackScope;

        private sealed class CallbackScope : IDisposable
        {
            public readonly Session Session;
            public readonly CallbackScope Previous;
            public CallbackScope(Session session)
            {
                Session = session;
                Previous = _CallbackScope;
                _CallbackScope = this;
            }
            public void Dispose() => _CallbackScope = Previous;
        }

        private static bool IsInCallback(Session session)
        {
            for (var scope = _CallbackScope; scope != null; scope = scope.Previous)
                if (ReferenceEquals(scope.Session, session))
                    return true;
            return false;
        }

        private sealed class Session
        {
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly TaskScheduler Scheduler = new LimitedConcurrencyLevelTaskScheduler(1);
            public readonly TaskCompletionSource<bool> Ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> Notified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskFactory Factory;
            public Task Initialization;
            public Task ConnectTask;
            public Thread Watcher;
            public bool Ready;
            public bool Stopping;
            public bool NotifyErrorReset;
            public int Publications;
            public TaskCompletionSource<bool> PublicationsDrained;
            public Exception Error;
            public Exception CleanupError;
        }

        internal long AssignmentGeneration { get; private set; }
        public Exception LastError { get { lock (_LifecycleLock) return _LastError; } }

        /// <summary>
        /// Creates a wrapper with the default 10 ms delay between polling passes.
        /// </summary>
        public ExRobotIF(RobotIF robot)
            : this(robot, 10)
        {
        }

        /// <summary>
        /// Creates a wrapper with a delay in milliseconds between polling passes.
        /// Values below 10 ms are silently raised to 10 ms. The delay applies
        /// after each pass, including empty passes, and is not a refresh period.
        /// </summary>
        public ExRobotIF(RobotIF robot, int pollingDelay)
        {
            _Robot = robot ?? throw new ArgumentNullException(nameof(robot));
            PollingDelay = Math.Max(10, pollingDelay);

            SDI = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalInputHolder(this, index, IRobotIFExtension.OFFSET_SDIO));
            SDO = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalOutputHolder(this, index, IRobotIFExtension.OFFSET_SDIO));
            RDI = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalInputHolder(this, index, IRobotIFExtension.OFFSET_RDIO));
            RDO = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalOutputHolder(this, index, IRobotIFExtension.OFFSET_RDIO));
            UI = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalInputHolder(this, index, IRobotIFExtension.OFFSET_UOP));
            UO = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalOutputHolder(this, index, IRobotIFExtension.OFFSET_UOP));
            SI = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalInputHolder(this, index, IRobotIFExtension.OFFSET_SOP));
            SO = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalOutputHolder(this, index, IRobotIFExtension.OFFSET_SOP));
            WI = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalInputHolder(this, index, IRobotIFExtension.OFFSET_WIO));
            WO = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalOutputHolder(this, index, IRobotIFExtension.OFFSET_WIO));
            WSI = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalInputHolder(this, index, IRobotIFExtension.OFFSET_WSI));
            PMC_K = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalOutputHolder(this, index, IRobotIFExtension.OFFSET_PMC_K));
            PMC_R = new RobotDataDictionary<int, RobotSignalHolderBase<bool>>(index => new DigitalOutputHolder(this, index, IRobotIFExtension.OFFSET_PMC_R));

            GI = new RobotDataDictionary<int, RobotSignalHolderBase<ushort>>(index => new GroupedInputHolder(this, index, IRobotIFExtension.OFFSET_GIO));
            GO = new RobotDataDictionary<int, RobotSignalHolderBase<ushort>>(index => new GroupedOutputHolder(this, index, IRobotIFExtension.OFFSET_GIO));
            AI = new RobotDataDictionary<int, RobotSignalHolderBase<ushort>>(index => new GroupedInputHolder(this, index, IRobotIFExtension.OFFSET_AIO));
            AO = new RobotDataDictionary<int, RobotSignalHolderBase<ushort>>(index => new GroupedOutputHolder(this, index, IRobotIFExtension.OFFSET_AIO));
            PMC_D = new RobotDataDictionary<int, RobotSignalHolderBase<ushort>>(index => new GroupedOutputHolder(this, index, IRobotIFExtension.OFFSET_PMC_D));

            R = new RobotDataDictionary<int, RobotWritableDataHolderBase<float>>(index => new RobotNumericRegisterHolder(this, index));
            PR = new RobotDataDictionary<int, RobotWritableDataHolderBase<Position>>(index => new RobotPositionRegisterHolder(this, index));
            SR = new RobotDataDictionary<int, RobotWritableDataHolderBase<string>>(index => new RobotStringRegisterHolder(this, index));

            SysVarI = new RobotDataDictionary<string, RobotWritableDataHolderBase<int>>(key => new RobotIntegerSystemVariableHolder(this, key));
            SysVarP = new RobotDataDictionary<string, RobotWritableDataHolderBase<Position>>(key => new RobotPositionSystemVariableHolder(this, key));
            SysVarS = new RobotDataDictionary<string, RobotWritableDataHolderBase<string>>(key => new RobotStringSystemVariableHolder(this, key));

            POS = new RobotDataDictionary<uint, RobotDataHolderBase<Position>>(index => new RobotCurrentPositionHolder(this, index % 10, index / 10));
            PRG = new RobotDataDictionary<int, RobotDataHolderBase<RobotTaskStatus>>(index => new RobotTaskHolder(this, index % 10000, (RobotTaskType)(index / 10000)));
            ALM = new RobotDataDictionary<int, RobotDataHolderBase<RobotAlarm>>(index => new RobotAlarmHolder(this, Math.Abs(index), index < 0));
        }

        /// <summary>The delay in milliseconds between polling passes.</summary>
        public int PollingDelay { get; }

        /// <summary>
        /// Connects using an approximate TCP connect timeout and per-send/receive
        /// timeout in milliseconds. Zero disables these timeouts. This is not an
        /// overall handshake deadline; hostname resolution may continue after a
        /// timeout on older targets.
        /// </summary>
        public Task ConnectAsync(string host, ushort port = RobotIF.DEFAULT_PORT, int timeout = RobotIF.DEFAULT_TIMEOUT)
        {
            lock (_LifecycleLock)
            {
                if (_Session != null)
                {
                    if (_Session.Stopping)
                        return ConnectAfterStopAsync(_Session, host, port, timeout, _ConnectVersion);
                    if (!_Session.Ready && TaskScheduler.Current == _Session.Scheduler)
                        return Task.FromException(new InvalidOperationException("Cannot connect from initialization."));
                    return _Session.ConnectTask;
                }

                var session = new Session();
                session.Factory = new TaskFactory(session.Cancellation.Token, TaskCreationOptions.None,
                    TaskContinuationOptions.None, session.Scheduler);
                _Session = session;
                session.NotifyErrorReset = _LastError != null;
                _LastError = null;
                session.Initialization = session.Factory.StartNew(() => Initialize(session, host, port, timeout));
                session.ConnectTask = Task.Run(() => CompleteConnectAsync(session));
                return session.ConnectTask;
            }
        }

        private async Task ConnectAfterStopAsync(Session session, string host, ushort port, int timeout, long version)
        {
            await session.Ended.Task.ConfigureAwait(false);
            Task connect;
            lock (_LifecycleLock)
            {
                if (version != _ConnectVersion)
                    throw new OperationCanceledException("The pending connection was canceled by disconnect.");
                connect = ConnectAsync(host, port, timeout);
            }
            await connect.ConfigureAwait(false);
        }

        private void Initialize(Session session, string host, ushort port, int timeout)
        {
            session.Cancellation.Token.ThrowIfCancellationRequested();
            _Robot.Connect(host, port, timeout);
            session.Cancellation.Token.ThrowIfCancellationRequested();
            _Robot.WriteCommand("CLRASG");
            AssignmentOffset = 1;
            AssignmentGeneration++;
            foreach (var holder in AllReadableSignals)
            {
                session.Cancellation.Token.ThrowIfCancellationRequested();
                if (holder is IRobotComplexDataHolder complex)
                    complex.EnsureAssignment();
            }
            session.Cancellation.Token.ThrowIfCancellationRequested();
        }

        private async Task CompleteConnectAsync(Session session)
        {
            try
            {
                await session.Initialization.ConfigureAwait(false);
                lock (_LifecycleLock)
                {
                    if (session.Stopping)
                        throw new OperationCanceledException();
                    session.Watcher = new Thread(() => Watch(session)) { IsBackground = true };
                    session.Ready = true;
                    session.Watcher.Start();
                }
                if (!EnterPublication(session))
                    throw new OperationCanceledException();
                try
                {
                    using (new CallbackScope(session))
                    {
                        if (session.NotifyErrorReset)
                            OnPropertyChanged(nameof(LastError));
                        OnPropertyChanged(nameof(IsConnected));
                        if (IsStopping(session))
                            throw new OperationCanceledException();
                    }
                }
                finally { LeavePublication(session); }
            }
            catch (Exception ex)
            {
                var stopping = IsStopping(session);
                StartStop(session, stopping ? null : ex);
                await session.Notified.Task.ConfigureAwait(false);
                if (!stopping && session.Error != null && !ReferenceEquals(session.Error, ex))
                    throw session.Error;
                throw;
            }
        }

        private bool IsStopping(Session session)
        {
            lock (_LifecycleLock) return session.Stopping;
        }

        private void Watch(Session session)
        {
            try
            {
                while (!session.Cancellation.IsCancellationRequested)
                {
                    if (!_Robot.IsConnected)
                        throw new IOException("Robot connection lost.");
                    foreach (var holder in AllReadableSignals)
                    {
                        session.Cancellation.Token.ThrowIfCancellationRequested();
                        holder.PullValue();
                    }
                    if (session.Cancellation.Token.WaitHandle.WaitOne(PollingDelay))
                        break;
                }
            }
            catch (Exception ex)
            {
                if (!IsStopping(session))
                    StartStop(session, ex);
            }
        }

        private void StartStop(Session session, Exception error)
        {
            lock (_LifecycleLock)
            {
                if (session.Stopping)
                {
                    if (error != null)
                        session.Error = Combine(session.Error, error);
                    return;
                }
                session.Stopping = true;
                session.Ready = false;
                session.Error = error;
            }

            Exception cleanupError = null;
            using (new CallbackScope(session))
            {
                try { session.Cancellation.Cancel(); }
                catch (Exception ex) { cleanupError = ex; }
                try { _Robot.Disconnect(); }
                catch (Exception ex) { cleanupError = Combine(cleanupError, ex); }
            }
            Task.Run(() => FinishStop(session, cleanupError));
        }

        private void FinishStop(Session session, Exception cleanupError)
        {
            try
            {
                using (new CallbackScope(session))
                {
                    // Connect can install its socket just after the first interrupt.
                    while (!session.Initialization.IsCompleted)
                    {
                        try { _Robot.Disconnect(); }
                        catch (Exception ex) { cleanupError = Combine(cleanupError, ex); }
                        try { session.Initialization.Wait(20); }
                        catch (AggregateException) { break; }
                    }
                    try { _Robot.Disconnect(); }
                    catch (Exception ex) { cleanupError = Combine(cleanupError, ex); }
                }
                try
                {
                    Task.Factory.StartNew(() => { }, CancellationToken.None,
                        TaskCreationOptions.None, session.Scheduler).GetAwaiter().GetResult();
                }
                catch (Exception ex) { cleanupError = Combine(cleanupError, ex); }
                WaitForPublications(session).GetAwaiter().GetResult();
                try { session.Watcher?.Join(); }
                catch (Exception ex) { cleanupError = Combine(cleanupError, ex); }
                session.Error = Combine(session.Error, cleanupError);
                session.CleanupError = cleanupError;
                lock (_LifecycleLock)
                {
                    _LastError = session.Error;
                }
            }
            finally
            {
                session.Cancellation.Dispose();
            }
            Exception notificationError = null;
            using (new CallbackScope(session))
            {
                try
                {
                    if (session.Error != null)
                        OnPropertyChanged(nameof(LastError));
                }
                catch (Exception ex) { notificationError = ex; }
                try { OnPropertyChanged(nameof(IsConnected)); }
                catch (Exception ex) { notificationError = Combine(notificationError, ex); }
            }
            if (notificationError != null)
            {
                session.CleanupError = Combine(session.CleanupError, notificationError);
                session.Error = Combine(session.Error, notificationError);
                lock (_LifecycleLock)
                {
                    if (ReferenceEquals(_Session, session))
                        _LastError = session.Error;
                }
            }
            lock (_LifecycleLock)
            {
                if (ReferenceEquals(_Session, session))
                    _Session = null;
            }
            session.Ended.TrySetResult(true);
            session.Notified.TrySetResult(true);
        }

        private bool EnterPublication(Session session)
        {
            lock (_LifecycleLock)
            {
                if (!ReferenceEquals(_Session, session) || !session.Ready || session.Stopping)
                    return false;
                ++session.Publications;
                return true;
            }
        }

        private void LeavePublication(Session session)
        {
            TaskCompletionSource<bool> drained = null;
            lock (_LifecycleLock)
            {
                if (--session.Publications == 0)
                    drained = session.PublicationsDrained;
            }
            drained?.TrySetResult(true);
        }

        private Task WaitForPublications(Session session)
        {
            lock (_LifecycleLock)
            {
                if (session.Publications == 0)
                    return Task.CompletedTask;
                return (session.PublicationsDrained ??=
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        private static Exception Combine(Exception first, Exception second) =>
            first == null ? second : second == null ? first : new AggregateException(first, second);

        private void _WriteAssignmentCommand(string command)
        {
            try { _Robot.WriteCommand(command); }
            catch (Exception ex)
            {
                Session session;
                lock (_LifecycleLock) session = _Session;
                if (session != null && !IsStopping(session))
                    StartStop(session, ex);
                throw;
            }
        }

        private Session CaptureReadySession()
        {
            lock (_LifecycleLock)
            {
                var session = _Session;
                return session != null && session.Ready && !session.Stopping ? session : null;
            }
        }

        private Task<T> _InvokeIfConnected<T>(Session expected, Func<T> func, [CallerMemberName] string caller = "Unknown")
        {
            lock (_LifecycleLock)
            {
                var session = _Session;
                if (expected == null || !ReferenceEquals(session, expected) || session.Stopping || !session.Ready)
                    return Task.FromException<T>(new IOException($"Cannot invoke `{caller}()` when not connected."));
                if (TaskScheduler.Current != session.Scheduler)
                    return session.Factory.StartNew(() =>
                    {
                        session.Cancellation.Token.ThrowIfCancellationRequested();
                        return func();
                    });
            }
            try { return Task.FromResult(func()); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }

        private Task _WriteSnapshot<T>(T[] values, Action<T[]> write, [CallerMemberName] string caller = "Unknown")
        {
            var session = CaptureReadySession();
            if (session == null)
                return Task.FromException(new IOException($"Cannot invoke `{caller}()` when not connected."));
            var snapshot = Snapshot(values);
            return _InvokeIfConnected(session, () => { write(snapshot); return true; }, caller);
        }

        public Task<bool[]> AsyncReadDI(int index, ushort count) => _InvokeIfConnected(CaptureReadySession(), () => _Robot.ReadDI(index, count));
        public Task<bool[]> AsyncReadDO(int index, ushort count) => _InvokeIfConnected(CaptureReadySession(), () => _Robot.ReadDO(index, count));
        public Task<ushort[]> AsyncReadGI(int index, ushort count) => _InvokeIfConnected(CaptureReadySession(), () => _Robot.ReadGI(index, count));
        public Task<ushort[]> AsyncReadGO(int index, ushort count) => _InvokeIfConnected(CaptureReadySession(), () => _Robot.ReadGO(index, count));
        public Task AsyncWriteDI(int index, params bool[] values) => _WriteSnapshot(values, snapshot => _Robot.WriteDI(index, snapshot));
        public Task AsyncWriteDO(int index, params bool[] values) => _WriteSnapshot(values, snapshot => _Robot.WriteDO(index, snapshot));
        public Task AsyncWriteGI(int index, params ushort[] values) => _WriteSnapshot(values, snapshot => _Robot.WriteGI(index, snapshot));
        public Task AsyncWriteGO(int index, params ushort[] values) => _WriteSnapshot(values, snapshot => _Robot.WriteGO(index, snapshot));

        public Task<byte[]> AsyncReadSNPX(int index, ushort count) => _InvokeIfConnected(CaptureReadySession(), () => _Robot.ReadSNPX(index, count));
        public Task AsyncWriteSNPX(int index, params byte[] values) => _WriteSnapshot(values, snapshot => _Robot.WriteSNPX(index, snapshot));
        public Task AsyncWriteCommand(string command) => _InvokeIfConnected(CaptureReadySession(), () => { _Robot.WriteCommand(command); return true; });

        public Task<string> AsyncExecuteKCL(string command) => _InvokeIfConnected(CaptureReadySession(), () => _Robot.ExecuteKCL(command));
        public Task<string> AsyncExecuteKarel(string program) => _InvokeIfConnected(CaptureReadySession(), () => _Robot.ExecuteKarel(program));

        private static T[] Snapshot<T>(T[] values) => values == null ? null : (T[])values.Clone();
    }
}
