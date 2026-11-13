using Fanuc.RobotInterface;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using Wpf.Ui.Appearance;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace FANUCRobotTest.UI
{
    public partial class MainWindow : FluentWindow
    {
        public class SettingsT : SettingsBase
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private double _Width, _Height;
            [DefaultValue(800.0)]
            public double Width { get => _Width; set => SetProperty(ref _Width, value); }
            [DefaultValue(450.0)]
            public double Height { get => _Height; set => SetProperty(ref _Height, value); }

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            WindowState _WindowState;
            [DefaultValue(WindowState.Normal)]
            public WindowState WindowState { get => _WindowState; set => SetProperty(ref _WindowState, value); }

            public SettingsT(string filename) : base(filename)
            {
            }
        }

        public SettingsT Settings { get; } = new SettingsT("Settings.MainWindow.json");

        public IExRobotIF RobotIF { get; } = new ExRobotIF(new RobotIF());

        public static readonly DependencyProperty RobotProperty = DependencyProperty.Register(nameof(Robot), typeof(IExRobotIF), typeof(MainWindow), new PropertyMetadata(null));

        public IExRobotIF Robot { get => (IExRobotIF)GetValue(RobotProperty); set => SetValue(RobotProperty, value); }

        public MainWindow()
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.None);
            InitializeComponent();
            Settings.PropertyChanged += (s, e) => Settings.Save();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Robot = RobotIF;

            //var proto = new ServiceRequestTransferProtocol();
            //proto.Test();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Robot.Disconnect();
            Application.Current.Shutdown();
        }

        private void Button_ConnectDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Robot.IsConnected)
                    Robot.Disconnect();
                else
                    Robot.Connect(App.Instance.Settings.RobotHost, App.Instance.Settings.RobotPort, App.Instance.Settings.RobotTimeout);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{ex}");
            }
        }

        private void _BenchmarkRun(Action action, [CallerMemberName] string caller = "Unknown")
        {
            try
            {
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                TextBlock_Info.Text = $"{caller}() took {sw.ElapsedMilliseconds}ms";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{ex}");
            }
        }

        private void Button_ClearAlarm_Click(object sender, RoutedEventArgs e) => _BenchmarkRun(() => Robot.ClearAlarm());
        private void Button_ResetAlarm_Click(object sender, RoutedEventArgs e) => _BenchmarkRun(() => Robot.ResetAlarm());


        private void Button_Test_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                //var proto = new ServiceRequestTransferProtocol();
                //proto.Test();
                //Robot.WriteSNPX("Garbage!!!!");
                //Robot.WriteSNPX("CLRASG");

                //Robot.WriteSNPX("SETASG 1 50 POS[0] 0.0");      // AddCurPos(1)
                //Robot.WriteSNPX("SETASG 51 50 POS[3] 0.0");     // AddCurPosUF(1, 3)
                //Robot.WriteSNPX("SETASG 101 50 POS[G2:5] 0.0"); // AddCurPosUF(2, 5)

                //Robot.WriteSNPX("SETASG 151 32 R[1] 1.0");      // AddIntReg(1, 16)
                //Robot.WriteSNPX("SETASG 183 32 R[1] 0");        // AddRealReg(1, 16)

                //Robot.WriteSNPX("SETASG 215 80 SR[1] 1");       // AddString(SR, 1, 2)

                //Robot.WriteSNPX("SETASG 295 80 SR[C1] 1");      // AddString(#SR, 1, 2)
                //Robot.WriteSNPX("SETASG 375 80 R[C1] 1");       // AddString(#R, 1, 2)
                //Robot.WriteSNPX("SETASG 455 80 PR[C1] 1");      // AddString(#PR, 1, 2)
                //Robot.WriteSNPX("SETASG 535 80 DI[C1] 1");      // AddString(#SDI, 1, 2)
                //Robot.WriteSNPX("SETASG 615 80 DO[C1] 1");      // AddString(#SDO, 1, 2)
                //Robot.WriteSNPX("SETASG 695 80 RI[C1] 1");      // AddString(#RDI, 1, 2)
                //Robot.WriteSNPX("SETASG 775 80 RO[C1] 1");      // AddString(#RDO, 1, 2)
                //Robot.WriteSNPX("SETASG 855 80 UI[C1] 1");      // AddString(#UI, 1, 2)
                //Robot.WriteSNPX("SETASG 935 80 UO[C1] 1");      // AddString(#UO, 1, 2)
                //Robot.WriteSNPX("SETASG 1015 80 SI[C1] 1");     // AddString(#SI, 1, 2)
                //Robot.WriteSNPX("SETASG 1095 80 SO[C1] 1");     // AddString(#SO, 1, 2)
                //Robot.WriteSNPX("SETASG 1175 80 WI[C1] 1");     // AddString(#WI, 1, 2)
                //Robot.WriteSNPX("SETASG 1255 80 WO[C1] 1");     // AddString(#WO, 1, 2)
                //Robot.WriteSNPX("SETASG 1335 80 WSI[C1] 1");    // AddString(#WSI, 1, 2)
                //Robot.WriteSNPX("SETASG 1415 80 GI[C1] 1");     // AddString(#GI, 1, 2)
                //Robot.WriteSNPX("SETASG 1495 80 GO[C1] 1");     // AddString(#GO, 1, 2)
                //Robot.WriteSNPX("SETASG 1575 80 AI[C1] 1");     // AddString(#AI, 1, 2)
                //Robot.WriteSNPX("SETASG 1655 80 AO[C1] 1");     // AddString(#AO, 1, 2)

                //Robot.WriteSNPX("SETASG 1735 100 PR[1] 0.0");   // AddPosReg(1, 2)

                //Robot.WriteSNPX("SETASG 1835 2 $RMT_MASTER 1");             // AddSysVar<ushort>("$RMT_MASTER")
                //Robot.WriteSNPX("SETASG 1837 40 $SHELL_WRK.$cust_name 1");  // AddSysVar<string>("$SHELL_WRK.$cust_name")
                //Robot.WriteSNPX("SETASG 1877 2 $SHELL_WRK.$cust_start 1");  // AddSysVar<ushort>("$SHELL_WRK.$cust_Start")
                //Robot.WriteSNPX("SETASG 1879 50 $LASTPAUSPOS[1] 0.0");      // AddSysVar<Position>("$LASTPAUSPOS[1]")

                //Robot.WriteSNPX("SETASG 1929 18 PRG[1] 1");     // AddTask(All, 1)
                //Robot.WriteSNPX("SETASG 1947 18 PRG[M1] 1");    // AddTask(IgnoreMacro, 1)
                //Robot.WriteSNPX("SETASG 1965 18 PRG[K1] 1");    // AddTask(IgnoreKarel, 1)
                //Robot.WriteSNPX("SETASG 1983 18 PRG[MK1] 1");   // AddTask(IgnoreMacroKarel, 1)

                //Robot.WriteSNPX("SETASG 2001 2000 ALM[1] 1");   // AddAlarm(Active, 20)
                //Robot.WriteSNPX("SETASG 4001 2000 ALM[E1] 1");  // AddAlarm(History, 20)

                //var bytes = Array.Empty<byte>();
                //bytes = Robot.ReadSNPX(101, 100);
                //bytes = Robot.ReadSNPX(1, 200);
                //bytes = Robot.ReadSNPX(1, 256);
                //bytes = Robot.ReadSNPX(1, 500);
                //bytes = Robot.ReadSNPX(1, 1000);
                //bytes = Robot.ReadSNPX(1, 1500);
                //bytes = Robot.ReadSNPX(1, 3000);
                //bytes = Robot.ReadSNPX(1, 6000);
                //bytes = Robot.ReadSNPX(1, 12000);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{ex}");
            }
        }
    }
}
