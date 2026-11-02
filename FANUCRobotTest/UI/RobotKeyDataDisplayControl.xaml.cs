using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace FANUCRobotTest.UI
{
    public class DataAddedEventArgs : EventArgs
    {
        public string DataName { get; init; }
        public string DataKey { get; init; }
    }

    /// <summary>
    /// RobotKeyDataDisplayControl.xaml 的互動邏輯
    /// </summary>
    public partial class RobotKeyDataDisplayControl : UserControl
    {
        public static readonly DependencyProperty DataNameProperty =
            DependencyProperty.Register(nameof(DataName), typeof(string), typeof(RobotKeyDataDisplayControl), new PropertyMetadata("Unknown"));
        public static readonly DependencyProperty SelectedDataKeyProperty =
            DependencyProperty.Register(nameof(SelectedDataKey), typeof(string), typeof(RobotKeyDataDisplayControl), new PropertyMetadata(""));

        public static readonly DependencyProperty DataItemsSourceProperty =
            DependencyProperty.Register(nameof(DataItemsSource), typeof(IEnumerable), typeof(RobotKeyDataDisplayControl), new PropertyMetadata(null));
        public static readonly DependencyProperty DataItemsPanelProperty =
            DependencyProperty.Register(nameof(DataItemsPanel), typeof(ItemsPanelTemplate), typeof(RobotKeyDataDisplayControl), new PropertyMetadata(null));
        public static readonly DependencyProperty DataItemTemplateProperty =
            DependencyProperty.Register(nameof(DataItemTemplate), typeof(DataTemplate), typeof(RobotKeyDataDisplayControl), new PropertyMetadata(null));
        public static readonly DependencyProperty DataItemTemplateSelectorProperty =
            DependencyProperty.Register(nameof(DataItemTemplateSelector), typeof(DataTemplateSelector), typeof(RobotKeyDataDisplayControl), new PropertyMetadata(null));

        public string DataName { get => (string)GetValue(DataNameProperty); set => SetValue(DataNameProperty, value); }
        public string SelectedDataKey { get => (string)GetValue(SelectedDataKeyProperty); set => SetValue(SelectedDataKeyProperty, value); }

        public IEnumerable DataItemsSource { get => (IEnumerable)GetValue(DataItemsSourceProperty); set => SetValue(DataItemsSourceProperty, value); }
        public ItemsPanelTemplate DataItemsPanel { get => (ItemsPanelTemplate)GetValue(DataItemsPanelProperty); set => SetValue(DataItemsPanelProperty, value); }
        public DataTemplate DataItemTemplate { get => (DataTemplate)GetValue(DataItemTemplateProperty); set => SetValue(DataItemTemplateProperty, value); }
        public DataTemplateSelector DataItemTemplateSelector { get => (DataTemplateSelector)GetValue(DataItemTemplateSelectorProperty); set => SetValue(DataItemTemplateSelectorProperty, value); }

        public event EventHandler<DataAddedEventArgs> DataAdded;

        public RobotKeyDataDisplayControl()
        {
            InitializeComponent();
        }

        private void Button_Add_Click(object sender, RoutedEventArgs e) =>
            DataAdded?.Invoke(this, new DataAddedEventArgs()
            {
                DataName = DataName,
                DataKey = SelectedDataKey,
            });
    }
}
