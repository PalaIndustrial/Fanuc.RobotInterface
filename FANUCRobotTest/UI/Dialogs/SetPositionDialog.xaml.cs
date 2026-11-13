using Fanuc.RobotInterface;
using System;
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

        public SetPositionDialog(Position source) : this()
        {
            ArgumentNullException.ThrowIfNull(source);
            Position = new Position
            {
                UserFrame = source.UserFrame,
                UserTool = source.UserTool,
                Joint = source.Joint == null ? null : new PositionJoint
                {
                    J1 = source.Joint.J1, J2 = source.Joint.J2, J3 = source.Joint.J3,
                    J4 = source.Joint.J4, J5 = source.Joint.J5, J6 = source.Joint.J6,
                    J7 = source.Joint.J7, J8 = source.Joint.J8, J9 = source.Joint.J9
                },
                Cartisian = source.Cartisian == null ? null : new PositionCartisian
                {
                    X = source.Cartisian.X, Y = source.Cartisian.Y, Z = source.Cartisian.Z,
                    W = source.Cartisian.W, P = source.Cartisian.P, R = source.Cartisian.R,
                    E1 = source.Cartisian.E1, E2 = source.Cartisian.E2, E3 = source.Cartisian.E3,
                    Config = source.Cartisian.Config,
                    T4 = source.Cartisian.T4, T5 = source.Cartisian.T5, T6 = source.Cartisian.T6
                }
            };
            DataContext = Position;
        }

    }
}
