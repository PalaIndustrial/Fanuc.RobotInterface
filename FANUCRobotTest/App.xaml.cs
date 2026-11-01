using Fanuc.RobotInterface;
using Fanuc.RobotInterface.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FANUCRobotTest
{
    public partial class App : Application
    {
        public class SettingsT : SettingsBase
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private string _RobotHost;
            [DefaultValue("127.0.0.2")]
            public string RobotHost { get => _RobotHost; set => SetProperty(ref _RobotHost, value); }

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private ushort _RobotPort;
            [DefaultValue(OfficialIF.DEFAULT_ROBOTIF_PORT)]
            public ushort RobotPort { get => _RobotPort; set => SetProperty(ref _RobotPort, value); }

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private int _RobotTimeout;
            [DefaultValue(OfficialIF.DEFAULT_TIMEOUT)]
            public int RobotTimeout { get => _RobotTimeout; set => SetProperty(ref _RobotTimeout, value); }

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private int _ReadIO_Index, _WriteIO_Index;
            [DefaultValue(1)]
            public int ReadIO_Index { get => _ReadIO_Index; set => SetProperty(ref _ReadIO_Index, value); }
            [DefaultValue(1)]
            public int WriteIO_Index { get => _WriteIO_Index; set => SetProperty(ref _WriteIO_Index, value); }

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public ushort _ReadIO_Count;
            [DefaultValue(8)]
            public ushort ReadIO_Count { get => _ReadIO_Count; set => SetProperty(ref _ReadIO_Count, value); }

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public string _WriteIO_Values;
            public string WriteIO_Values { get => _WriteIO_Values; set => SetProperty(ref _WriteIO_Values, value); }

            public SettingsT(string filename) :
                base(filename)
            {
            }
        }

        public SettingsT Settings { get; } = new SettingsT("Settings.App.json");

        public static App Instance => (App)Current;

        public App()
        {
            Settings.PropertyChanged += (s, e) => Settings.Save();
        }
    }
}
