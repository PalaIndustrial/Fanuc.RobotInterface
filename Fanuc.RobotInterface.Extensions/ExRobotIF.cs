using Fanuc.RobotInterface.Collections;
using System;
using System.ComponentModel;
using System.Diagnostics;
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

        private TaskFactory _TaskFactory;
        private Thread _WatcherThread;
        private CancellationTokenSource _CancellationTokenSource = new CancellationTokenSource();

        public ExRobotIF(RobotIF robot)
        {
            _Robot = robot ?? throw new ArgumentNullException(nameof(robot));
            _CancellationTokenSource.Cancel();

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

        public Task ConnectAsync(string host, ushort port = RobotIF.DEFAULT_PORT, int timeout = RobotIF.DEFAULT_TIMEOUT)
        {
            if (IsConnected)
                return Task.CompletedTask;

            var scheduler = new LimitedConcurrencyLevelTaskScheduler(1);
            _CancellationTokenSource = new CancellationTokenSource();
            _TaskFactory = new TaskFactory(_CancellationTokenSource.Token, TaskCreationOptions.None, TaskContinuationOptions.None, scheduler);

            return _TaskFactory.StartNew(() =>
            {
                _Robot.Connect(host, port, timeout);
                if (_Robot.IsConnected)
                {
                    _Robot.WriteCommand("CLRASG");
                    foreach (var v in AllReadableSignals)
                    {
                        if (v is IRobotComplexDataHolder c)
                            c.WriteAssignment();
                    }

                    _WatcherThread = new Thread(() =>
                    {
#if DEBUG
                        var sw1 = Stopwatch.StartNew();
                        var sw2 = new Stopwatch();
                        var i = 0;
#endif
                        while (_Robot.IsConnected)
                        {
                            try
                            {
#if DEBUG
                                ++i;
                                sw2.Start();
#endif

                                foreach (var v in AllWritableSignals)
                                    v.PushValue();
                                foreach (var v in AllReadableSignals)
                                    v.PullValue();

#if DEBUG
                                sw2.Stop();

                                if (sw1.ElapsedMilliseconds > 1000)
                                {
                                    Debug.WriteLine($"{i} updates took {sw2.ElapsedMilliseconds}ms, average = {sw2.ElapsedMilliseconds / i}ms");
                                    sw1.Reset();
                                    sw2.Reset();
                                    sw1.Start();
                                    i = 0;
                                }
#endif
                            }
                            catch
                            {
                                Disconnect();
                            }
                        }
                    });
                    _WatcherThread.Start();
                }
                OnPropertyChanged(nameof(IsConnected));
            });
        }

        private Task _InvokeIfConnected(Action action, [CallerMemberName] string caller = "Unknown") =>
            _TaskFactory?.StartNew(action) ?? Task.FromException(new IOException($"Cannot invoke `{caller}()` when not connected."));
        private Task<T> _InvokeIfConnected<T>(Func<T> func, [CallerMemberName] string caller = "Unknown") =>
            _TaskFactory?.StartNew(func) ?? Task.FromException<T>(new IOException($"Cannot invoke `{caller}()` when not connected."));

        public Task<bool[]> AsyncReadDI(int index, ushort count) => _InvokeIfConnected(() => _Robot.ReadDI(index, count));
        public Task<bool[]> AsyncReadDO(int index, ushort count) => _InvokeIfConnected(() => _Robot.ReadDO(index, count));
        public Task<ushort[]> AsyncReadGI(int index, ushort count) => _InvokeIfConnected(() => _Robot.ReadGI(index, count));
        public Task<ushort[]> AsyncReadGO(int index, ushort count) => _InvokeIfConnected(() => _Robot.ReadGO(index, count));
        public Task AsyncWriteDI(int index, params bool[] values) => _InvokeIfConnected(() => _Robot.WriteDI(index, values));
        public Task AsyncWriteDO(int index, params bool[] values) => _InvokeIfConnected(() => _Robot.WriteDO(index, values));
        public Task AsyncWriteGI(int index, params ushort[] values) => _InvokeIfConnected(() => _Robot.WriteGI(index, values));
        public Task AsyncWriteGO(int index, params ushort[] values) => _InvokeIfConnected(() => _Robot.WriteGO(index, values));

        public Task<byte[]> AsyncReadSNPX(int index, ushort count) => _InvokeIfConnected(() => _Robot.ReadSNPX(index, count));
        public Task AsyncWriteSNPX(int index, params byte[] values) => _InvokeIfConnected(() => _Robot.WriteSNPX(index, values));
        public Task AsyncWriteCommand(string command) => _InvokeIfConnected(() => _Robot.WriteCommand(command));

        public Task<string> AsyncExecuteKCL(string command) => _InvokeIfConnected(() => _Robot.ExecuteKCL(command));
        public Task<string> AsyncExecuteKarel(string program) => _InvokeIfConnected(() => _Robot.ExecuteKarel(program));
    }
}
