using System.ComponentModel;

namespace Fanuc.RobotInterface
{
    public partial class ExRobotIF : IRobotIF, INotifyPropertyChanged
    {
        public bool IsConnected => _Robot.IsConnected;

        public void Connect(string host, ushort port = RobotIF.DEFAULT_PORT, int timeout = RobotIF.DEFAULT_TIMEOUT)
            => ConnectAsync(host, port, timeout).Wait();

        public void Disconnect()
        {
            if (IsConnected)
            {
                _TaskFactory?.StartNew(() => _Robot.Disconnect())?.Wait();
                _CancellationTokenSource?.Cancel();

                _TaskFactory = null;

                OnPropertyChanged(nameof(IsConnected));
            }
        }

        public void ClearAlarm() => _InvokeIfConnected(() => _Robot.ClearAlarm()).Wait();
        public void ResetAlarm() => _InvokeIfConnected(() => _Robot.ResetAlarm()).Wait();

        public bool[] ReadDI(int index, ushort count) => AsyncReadDI(index, count).Result;
        public bool[] ReadDO(int index, ushort count) => AsyncReadDO(index, count).Result;
        public ushort[] ReadGI(int index, ushort count) => AsyncReadGI(index, count).Result;
        public ushort[] ReadGO(int index, ushort count) => AsyncReadGO(index, count).Result;
        public void WriteDI(int index, params bool[] values) => AsyncWriteDI(index, values).Wait();
        public void WriteDO(int index, params bool[] values) => AsyncWriteDO(index, values).Wait();
        public void WriteGI(int index, params ushort[] values) => AsyncWriteGI(index, values).Wait();
        public void WriteGO(int index, params ushort[] values) => AsyncWriteGO(index, values).Wait();

        public byte[] ReadSNPX(int index, ushort count) => AsyncReadSNPX(index, count).Result;
        public void WriteSNPX(int index, params byte[] values) => AsyncWriteSNPX(index, values).Wait();
        public void WriteCommand(string command) => AsyncWriteCommand(command).Wait();

        public string ExecuteKCL(string command) => AsyncExecuteKCL(command).Result;
        public string ExecuteKarel(string program) => AsyncExecuteKarel(program).Result;
    }
}
