using Fanuc.RobotInterface;
using Wpf.Ui.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FANUCRobotTest.UI
{
    /// <summary>
    /// SetPositionDialog.xaml 的互動邏輯
    /// </summary>
    public partial class SetPositionDialog : ContentDialog
    {
        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(nameof(Position), typeof(Position), typeof(SetPositionDialog), new PropertyMetadata(null));

        public Position Position { get => (Position)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

        public SetPositionDialog()
        {
            InitializeComponent();
        }

    }
}
