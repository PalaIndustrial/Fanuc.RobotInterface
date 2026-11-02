using Fanuc.RobotInterface;
using System.Windows;
using Wpf.Ui.Controls;

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
