using Fanuc.RobotInterface;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ContentDialogHost = Wpf.Ui.Controls.ContentDialogHost;
using ContentDialogResult = Wpf.Ui.Controls.ContentDialogResult;

namespace FANUCRobotTest.UI
{
    public partial class RobotDisplayExtended : UserControl
    {
        public class SettingsT : SettingsBase
        {
            public ObservableCollection<int> SDI { get; } = new();
            public ObservableCollection<int> SDO { get; } = new();
            public ObservableCollection<int> RDI { get; } = new();
            public ObservableCollection<int> RDO { get; } = new();
            public ObservableCollection<int> UI { get; } = new();
            public ObservableCollection<int> UO { get; } = new();
            public ObservableCollection<int> SI { get; } = new();
            public ObservableCollection<int> SO { get; } = new();
            public ObservableCollection<int> WI { get; } = new();
            public ObservableCollection<int> WO { get; } = new();
            public ObservableCollection<int> WSI { get; } = new();
            public ObservableCollection<int> PMC_K { get; } = new();
            public ObservableCollection<int> PMC_R { get; } = new();

            public ObservableCollection<int> GI { get; } = new();
            public ObservableCollection<int> GO { get; } = new();
            public ObservableCollection<int> AI { get; } = new();
            public ObservableCollection<int> AO { get; } = new();
            public ObservableCollection<int> PMC_D { get; } = new();

            public ObservableCollection<int> R { get; } = new();
            public ObservableCollection<int> PR { get; } = new();
            public ObservableCollection<int> SR { get; } = new();

            public ObservableCollection<string> SysVarI { get; } = new();
            public ObservableCollection<string> SysVarP { get; } = new();
            public ObservableCollection<string> SysVarS { get; } = new();

            public ObservableCollection<uint> POS { get; } = new();
            public ObservableCollection<int> PRG { get; } = new();
            public ObservableCollection<int> ALM { get; } = new();

            public SettingsT(string filename) :
                base(filename)
            {
                SDI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SDI));
                SDO.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SDO));
                RDI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(RDI));
                RDO.CollectionChanged += (s, e) => OnPropertyChanged(nameof(RDO));
                UI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(UI));
                UO.CollectionChanged += (s, e) => OnPropertyChanged(nameof(UO));
                SI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SI));
                SO.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SO));
                WI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(WI));
                WO.CollectionChanged += (s, e) => OnPropertyChanged(nameof(WO));
                WSI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(WSI));
                PMC_K.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PMC_K));
                PMC_R.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PMC_R));

                GI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(GI));
                GO.CollectionChanged += (s, e) => OnPropertyChanged(nameof(GO));
                AI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(AI));
                AO.CollectionChanged += (s, e) => OnPropertyChanged(nameof(AO));
                PMC_D.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PMC_D));

                R.CollectionChanged += (s, e) => OnPropertyChanged(nameof(R));
                PR.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PR));
                SR.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SR));

                SysVarI.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SysVarI));
                SysVarP.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SysVarP));
                SysVarS.CollectionChanged += (s, e) => OnPropertyChanged(nameof(SysVarS));

                POS.CollectionChanged += (s, e) => OnPropertyChanged(nameof(POS));
                PRG.CollectionChanged += (s, e) => OnPropertyChanged(nameof(PRG));
                ALM.CollectionChanged += (s, e) => OnPropertyChanged(nameof(ALM));
            }
        }

        public SettingsT Settings { get; } = new SettingsT("Settings.RobotDisplayExtended.json");

        public static readonly DependencyProperty RobotProperty =
            DependencyProperty.Register(nameof(Robot), typeof(IExRobotIF), typeof(RobotDisplayExtended), new PropertyMetadata(null, RobotDIsplayExtended_DependencyPropertyChanged));
        public static readonly DependencyProperty DebugInfoProperty =
            DependencyProperty.Register(nameof(DebugInfo), typeof(string), typeof(RobotDisplayExtended), new PropertyMetadata(null));

        public IExRobotIF Robot { get => (IExRobotIF)GetValue(RobotProperty); set => SetValue(RobotProperty, value); }
        public string DebugInfo { get => (string)GetValue(DebugInfoProperty); set => SetValue(DebugInfoProperty, value); }

        private static void RobotDIsplayExtended_DependencyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (RobotDisplayExtended)d;
            switch (e.Property.Name)
            {
                case nameof(Robot):
                    if (e.NewValue is IExRobotIF new_robot)
                    {
                        foreach (var i in control.Settings.SDI) new_robot.SDI.GetOrAdd(i);
                        foreach (var i in control.Settings.SDO) new_robot.SDO.GetOrAdd(i);
                        foreach (var i in control.Settings.RDI) new_robot.RDI.GetOrAdd(i);
                        foreach (var i in control.Settings.RDO) new_robot.RDO.GetOrAdd(i);
                        foreach (var i in control.Settings.UI) new_robot.UI.GetOrAdd(i);
                        foreach (var i in control.Settings.UO) new_robot.UO.GetOrAdd(i);
                        foreach (var i in control.Settings.SI) new_robot.SI.GetOrAdd(i);
                        foreach (var i in control.Settings.SO) new_robot.SO.GetOrAdd(i);
                        foreach (var i in control.Settings.WI) new_robot.WI.GetOrAdd(i);
                        foreach (var i in control.Settings.WO) new_robot.WO.GetOrAdd(i);
                        foreach (var i in control.Settings.WSI) new_robot.WSI.GetOrAdd(i);
                        foreach (var i in control.Settings.PMC_K) new_robot.PMC_K.GetOrAdd(i);
                        foreach (var i in control.Settings.PMC_R) new_robot.PMC_R.GetOrAdd(i);

                        foreach (var i in control.Settings.GI) new_robot.GI.GetOrAdd(i);
                        foreach (var i in control.Settings.GO) new_robot.GO.GetOrAdd(i);
                        foreach (var i in control.Settings.AI) new_robot.AI.GetOrAdd(i);
                        foreach (var i in control.Settings.AO) new_robot.AO.GetOrAdd(i);
                        foreach (var i in control.Settings.PMC_D) new_robot.PMC_D.GetOrAdd(i);

                        foreach (var i in control.Settings.R) new_robot.R.GetOrAdd(i);
                        foreach (var i in control.Settings.PR) new_robot.PR.GetOrAdd(i);
                        foreach (var i in control.Settings.SR) new_robot.SR.GetOrAdd(i);

                        foreach (var k in control.Settings.SysVarI) new_robot.SysVarI.GetOrAdd(k);
                        foreach (var k in control.Settings.SysVarP) new_robot.SysVarP.GetOrAdd(k);
                        foreach (var k in control.Settings.SysVarS) new_robot.SysVarS.GetOrAdd(k);

                        foreach (var i in control.Settings.POS) new_robot.POS.GetOrAdd(i);
                        foreach (var i in control.Settings.PRG) new_robot.PRG.GetOrAdd(i);
                        foreach (var i in control.Settings.ALM) new_robot.ALM.GetOrAdd(i);
                    }
                    break;
            }
        }

        public RobotDisplayExtended()
        {
            InitializeComponent();
            Settings.PropertyChanged += (s, e) => Settings.Save();
        }

        private void RobotDataDisplayControl_SignalAdded(object sender, SignalAddedEventArgs e)
        {
            switch (e.SignalName)
            {
                // Bits
                case nameof(Settings.SDI):
                    if (!Settings.SDI.Contains(e.SignalIndex))
                    {
                        Settings.SDI.Add(e.SignalIndex);
                        Robot.SDI.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.SDO):
                    if (!Settings.SDO.Contains(e.SignalIndex))
                    {
                        Settings.SDO.Add(e.SignalIndex);
                        Robot.SDO.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.RDI):
                    if (!Settings.RDI.Contains(e.SignalIndex))
                    {
                        Settings.RDI.Add(e.SignalIndex);
                        Robot.RDI.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.RDO):
                    if (!Settings.RDO.Contains(e.SignalIndex))
                    {
                        Settings.RDO.Add(e.SignalIndex);
                        Robot.RDO.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.UI):
                    if (!Settings.UI.Contains(e.SignalIndex))
                    {
                        Settings.UI.Add(e.SignalIndex);
                        Robot.UI.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.UO):
                    if (!Settings.UO.Contains(e.SignalIndex))
                    {
                        Settings.UO.Add(e.SignalIndex);
                        Robot.UO.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.SI):
                    if (!Settings.SI.Contains(e.SignalIndex))
                    {
                        Settings.SI.Add(e.SignalIndex);
                        Robot.SI.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.SO):
                    if (!Settings.SO.Contains(e.SignalIndex))
                    {
                        Settings.SO.Add(e.SignalIndex);
                        Robot.SO.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.WI):
                    if (!Settings.WI.Contains(e.SignalIndex))
                    {
                        Settings.WI.Add(e.SignalIndex);
                        Robot.WI.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.WO):
                    if (!Settings.WO.Contains(e.SignalIndex))
                    {
                        Settings.WO.Add(e.SignalIndex);
                        Robot.WO.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.WSI):
                    if (!Settings.WSI.Contains(e.SignalIndex))
                    {
                        Settings.WSI.Add(e.SignalIndex);
                        Robot.WSI.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.PMC_K):
                    if (!Settings.PMC_K.Contains(e.SignalIndex))
                    {
                        Settings.PMC_K.Add(e.SignalIndex);
                        Robot.PMC_K.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.PMC_R):
                    if (!Settings.PMC_R.Contains(e.SignalIndex))
                    {
                        Settings.PMC_R.Add(e.SignalIndex);
                        Robot.PMC_R.GetOrAdd(e.SignalIndex);
                    }
                    break;
                // Words
                case nameof(Settings.GI):
                    if (!Settings.GI.Contains(e.SignalIndex))
                    {
                        Settings.GI.Add(e.SignalIndex);
                        Robot.GI.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.GO):
                    if (!Settings.GO.Contains(e.SignalIndex))
                    {
                        Settings.GO.Add(e.SignalIndex);
                        Robot.GO.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.AI):
                    if (!Settings.AI.Contains(e.SignalIndex))
                    {
                        Settings.AI.Add(e.SignalIndex);
                        Robot.AI.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.AO):
                    if (!Settings.AO.Contains(e.SignalIndex))
                    {
                        Settings.AO.Add(e.SignalIndex);
                        Robot.AO.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.PMC_D):
                    if (!Settings.PMC_D.Contains(e.SignalIndex))
                    {
                        Settings.PMC_D.Add(e.SignalIndex);
                        Robot.PMC_D.GetOrAdd(e.SignalIndex);
                    }
                    break;
                // Registers
                case nameof(Settings.R):
                    if (!Settings.R.Contains(e.SignalIndex))
                    {
                        Settings.R.Add(e.SignalIndex);
                        Robot.R.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.PR):
                    if (!Settings.PR.Contains(e.SignalIndex))
                    {
                        Settings.PR.Add(e.SignalIndex);
                        Robot.PR.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.SR):
                    if (!Settings.SR.Contains(e.SignalIndex))
                    {
                        Settings.SR.Add(e.SignalIndex);
                        Robot.SR.GetOrAdd(e.SignalIndex);
                    }
                    break;
                // Current States
                case nameof(Settings.POS):
                    if (!Settings.POS.Contains((uint)e.SignalIndex))
                    {
                        Settings.POS.Add((uint)e.SignalIndex);
                        Robot.POS.GetOrAdd((uint)e.SignalIndex);
                    }
                    break;
                case nameof(Settings.PRG):
                    if (!Settings.PRG.Contains(e.SignalIndex))
                    {
                        Settings.PRG.Add(e.SignalIndex);
                        Robot.PRG.GetOrAdd(e.SignalIndex);
                    }
                    break;
                case nameof(Settings.ALM):
                    if (!Settings.ALM.Contains(e.SignalIndex))
                    {
                        Settings.ALM.Add(e.SignalIndex);
                        Robot.ALM.GetOrAdd(e.SignalIndex);
                    }
                    break;
            }
        }

        private void RobotKeyDataDisplayControl_DataAdded(object sender, DataAddedEventArgs e)
        {
            switch (e.DataName)
            {
                // SystemVariables
                case nameof(Settings.SysVarI):
                    if (!Settings.SysVarI.Contains(e.DataKey))
                    {
                        Settings.SysVarI.Add(e.DataKey);
                        Robot.SysVarI.GetOrAdd(e.DataKey);
                    }
                    break;
                case nameof(Settings.SysVarP):
                    if (!Settings.SysVarP.Contains(e.DataKey))
                    {
                        Settings.SysVarP.Add(e.DataKey);
                        Robot.SysVarP.GetOrAdd(e.DataKey);
                    }
                    break;
                case nameof(Settings.SysVarS):
                    if (!Settings.SysVarS.Contains(e.DataKey))
                    {
                        Settings.SysVarS.Add(e.DataKey);
                        Robot.SysVarS.GetOrAdd(e.DataKey);
                    }
                    break;
            }
        }

        private async void Button_PosReg_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var holder = (ExRobotIF.RobotWritableDataHolderBase<Position>)button.Tag;
            var pos = new Position()
            {
                Cartisian = holder.Value.Cartisian,
                Joint = holder.Value.Joint,
                UserFrame = holder.Value.UserFrame,
                UserTool = holder.Value.UserTool,
            };
            try
            {
                button.IsEnabled = false;

                var dialog = new SetPositionDialog();
                dialog.DataContext = pos;
                dialog.DialogHostEx = ContentDialogHost.GetForWindow(Window.GetWindow(this))
                    ?? throw new InvalidOperationException("The window does not have a content dialog host.");
                var result = await dialog.ShowAsync();
                switch (result)
                {
                    case ContentDialogResult.Primary: // joint
                        pos.Cartisian = null;
                        holder.Value = pos;
                        break;
                    case ContentDialogResult.Secondary: // cartisian
                        pos.Joint = null;
                        holder.Value = pos;
                        break;
                    case ContentDialogResult.None:
                        break;
                }
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }
}
