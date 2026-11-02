using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace FANUCRobotTest.UI
{
    public class SignalAddedEventArgs : EventArgs
    {
        public string SignalName { get; init; }
        public int SignalIndex { get; init; }
    }

    public partial class RobotIndexDataDisplayControl : UserControl
    {
        public static readonly DependencyProperty SignalNameProperty =
            DependencyProperty.Register(nameof(SignalName), typeof(string), typeof(RobotIndexDataDisplayControl), new PropertyMetadata("Unknown"));
        public static readonly DependencyProperty SelectedSignalIndexProperty =
            DependencyProperty.Register(nameof(SelectedSignalIndex), typeof(int), typeof(RobotIndexDataDisplayControl), new PropertyMetadata(1));
        public static readonly DependencyProperty SignalItemsSourceProperty =
            DependencyProperty.Register(nameof(SignalItemsSource), typeof(IEnumerable), typeof(RobotIndexDataDisplayControl), new PropertyMetadata(null));
        public static readonly DependencyProperty SignalItemsPanelProperty =
            DependencyProperty.Register(nameof(SignalItemsPanel), typeof(ItemsPanelTemplate), typeof(RobotIndexDataDisplayControl), new PropertyMetadata(null));
        public static readonly DependencyProperty SignalItemTemplateProperty =
            DependencyProperty.Register(nameof(SignalItemTemplate), typeof(DataTemplate), typeof(RobotIndexDataDisplayControl), new PropertyMetadata(null));
        public static readonly DependencyProperty SignalItemTemplateSelectorProperty =
            DependencyProperty.Register(nameof(SignalItemTemplateSelector), typeof(DataTemplateSelector), typeof(RobotIndexDataDisplayControl), new PropertyMetadata(null));

        public string SignalName { get => (string)GetValue(SignalNameProperty); set => SetValue(SignalNameProperty, value); }
        public int SelectedSignalIndex { get => (int)GetValue(SelectedSignalIndexProperty); set => SetValue(SelectedSignalIndexProperty, value); }

        public IEnumerable SignalItemsSource { get => (IEnumerable)GetValue(SignalItemsSourceProperty); set => SetValue(SignalItemsSourceProperty, value); }
        public ItemsPanelTemplate SignalItemsPanel { get => (ItemsPanelTemplate)GetValue(SignalItemsPanelProperty); set => SetValue(SignalItemsPanelProperty, value); }
        public DataTemplate SignalItemTemplate { get => (DataTemplate)GetValue(SignalItemTemplateProperty); set => SetValue(SignalItemTemplateProperty, value); }
        public DataTemplateSelector SignalItemTemplateSelector { get => (DataTemplateSelector)GetValue(SignalItemTemplateSelectorProperty); set => SetValue(SignalItemTemplateSelectorProperty, value); }

        public event EventHandler<SignalAddedEventArgs> SignalAdded;

        public RobotIndexDataDisplayControl()
        {
            InitializeComponent();
        }

        private void Button_Add_Click(object sender, RoutedEventArgs e) =>
            SignalAdded?.Invoke(this, new SignalAddedEventArgs()
            {
                SignalName = SignalName,
                SignalIndex = SelectedSignalIndex,
            });
    }
}
