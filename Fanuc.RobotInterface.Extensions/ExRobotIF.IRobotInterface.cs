using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace Fanuc.RobotInterface
{
    public partial class ExRobotIF : IRobotIF, INotifyPropertyChanged
    {
        public bool IsConnected
        {
            get
            {
                lock (_LifecycleLock)
                    return _Session != null && _Session.Ready && _Robot.IsConnected;
            }
        }
        public string Host => ((IRobotIF)_Robot).Host;

        /// <inheritdoc />
        public void Connect(string host, ushort port = RobotIF.DEFAULT_PORT, int timeout = RobotIF.DEFAULT_TIMEOUT)
        {
            lock (_LifecycleLock)
            {
                if (_Session != null && (IsInCallback(_Session) && _Session.Stopping ||
                    (_Session.Stopping && (Thread.CurrentThread == _Session.Watcher ||
                     TaskScheduler.Current == _Session.Scheduler))))
                    throw new InvalidOperationException("Cannot synchronously reconnect from a stopping session.");
                if (_Session != null && TaskScheduler.Current == _Session.Scheduler &&
                    (!_Session.Ready || !_Session.ConnectTask.IsCompleted))
                    throw new InvalidOperationException("Cannot synchronously connect from initialization.");
                if (_Session != null && !_Session.ConnectTask.IsCompleted &&
                    (IsInCallback(_Session) || Thread.CurrentThread == _Session.Watcher))
                    throw new InvalidOperationException("Cannot synchronously connect from a connection callback.");
            }
            ConnectAsync(host, port, timeout).GetAwaiter().GetResult();
        }

        public void Disconnect()
        {
            Session session;
            lock (_LifecycleLock)
            {
                ++_ConnectVersion;
                session = _Session;
            }
            if (session == null)
                return;
            StartStop(session, null);
            if (IsInCallback(session) ||
                Thread.CurrentThread == session.Watcher ||
                TaskScheduler.Current == session.Scheduler)
                return;
            session.Notified.Task.GetAwaiter().GetResult();
            if (session.CleanupError != null)
                throw session.CleanupError;
        }

        public void ClearAlarm() => _InvokeIfConnected(CaptureReadySession(), () => { _Robot.ClearAlarm(); return true; }).GetAwaiter().GetResult();

        public bool[] ReadDI(int index, ushort count) => AsyncReadDI(index, count).GetAwaiter().GetResult();
        public bool[] ReadDO(int index, ushort count) => AsyncReadDO(index, count).GetAwaiter().GetResult();
        public ushort[] ReadGI(int index, ushort count) => AsyncReadGI(index, count).GetAwaiter().GetResult();
        public ushort[] ReadGO(int index, ushort count) => AsyncReadGO(index, count).GetAwaiter().GetResult();
        public void WriteDI(int index, params bool[] values) => AsyncWriteDI(index, values).GetAwaiter().GetResult();
        public void WriteDO(int index, params bool[] values) => AsyncWriteDO(index, values).GetAwaiter().GetResult();
        public void WriteGI(int index, params ushort[] values) => AsyncWriteGI(index, values).GetAwaiter().GetResult();
        public void WriteGO(int index, params ushort[] values) => AsyncWriteGO(index, values).GetAwaiter().GetResult();

        public byte[] ReadSNPX(int index, ushort count) => AsyncReadSNPX(index, count).GetAwaiter().GetResult();
        public void WriteSNPX(int index, params byte[] values) => AsyncWriteSNPX(index, values).GetAwaiter().GetResult();
        public void WriteCommand(string command) => AsyncWriteCommand(command).GetAwaiter().GetResult();
    }
}