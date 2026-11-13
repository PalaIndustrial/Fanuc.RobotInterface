using Fanuc.RobotInterface;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FANUCRobotTest.UI
{
    public enum WritableTextType { Word, Numeric, Integer, String }

    public class WritableTextBox : TextBox
    {
        public static readonly DependencyProperty HolderProperty = DependencyProperty.Register(
            nameof(Holder), typeof(IWritableValueHolder), typeof(WritableTextBox),
            new PropertyMetadata(null, HolderChanged));

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly SemaphoreSlim _writes = new(1, 1);
        private bool _updating;
        private bool _dirty;
        private int _revision;
        private int _outstanding;
        private bool _refreshPending;
        private bool _failed;
        private int _editRevision;
        private WritableTextType _valueType;

        public IWritableValueHolder Holder
        {
            get => (IWritableValueHolder)GetValue(HolderProperty);
            set => SetValue(HolderProperty, value);
        }

        public WritableTextType ValueType
        {
            get => _valueType;
            set
            {
                _valueType = value;
                if (Holder != null && !_dirty && !_failed && _outstanding == 0)
                    ShowValue();
            }
        }

        public WritableTextBox()
        {
            _timer.Tick += Commit;
            TextChanged += OnTextChanged;
            LostKeyboardFocus += OnLostKeyboardFocus;
            Unloaded += Commit;
        }

        private static async void HolderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (WritableTextBox)d;
            try
            {
                var pending = box.CommitAsync((IWritableValueHolder)e.OldValue);
                if (box._dirty)
                {
                    MessageBox.Show("The invalid edit could not be written.", "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
                    box._dirty = false;
                }
                box._revision++;
                if (e.OldValue is INotifyPropertyChanged oldHolder)
                    PropertyChangedEventManager.RemoveHandler(oldHolder, box.HolderValueChanged, nameof(IValueHolder.Value));
                if (e.NewValue is INotifyPropertyChanged newHolder)
                    PropertyChangedEventManager.AddHandler(newHolder, box.HolderValueChanged, nameof(IValueHolder.Value));
                if (box._failed)
                    box.IsReadOnly = true;
                else
                {
                    box.ClearError();
                    box.ShowValue();
                }
                await pending;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not write value: {ex.Message}", "Write failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void HolderValueChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    await Dispatcher.InvokeAsync(() => RefreshValue(sender));
                    return;
                }
                RefreshValue(sender);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not display value: {ex.Message}", "Display error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshValue(object sender)
        {
            if (!ReferenceEquals(sender, Holder))
                return;
            if (!_dirty && !_failed && _outstanding == 0 && !IsKeyboardFocusWithin)
                ShowValue();
            else
                _refreshPending = true;
        }

        private void ShowValue()
        {
            _updating = true;
            try
            {
                _refreshPending = false;
                var value = Holder?.Value;
                Text = ValueType == WritableTextType.Numeric && value is float number
                    ? number.ToString("0.000", Language.GetSpecificCulture())
                    : Convert.ToString(value, Language.GetSpecificCulture()) ?? "";
            }
            finally { _updating = false; }
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating || Holder == null)
                return;
            _revision++;
            _editRevision++;
            _dirty = true;
            _refreshPending = false;
            _failed = false;
            ClearError();
            _timer.Stop();
            _timer.Start();
        }

        private async void Commit(object sender, EventArgs e)
        {
            try { await CommitAsync(Holder); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not write value: {ex.Message}", "Write failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (!_dirty && !_failed && _outstanding == 0 && _refreshPending)
                ShowValue();
            else if (_dirty)
                Commit(sender, e);
        }

        private async Task CommitAsync(IWritableValueHolder holder)
        {
            _timer.Stop();
            if (!_dirty || holder == null)
                return;

            var text = Text;
            var revision = _revision;
            var editRevision = _editRevision;
            object value;
            try
            {
                var culture = Language.GetSpecificCulture();
                value = ValueType switch
                {
                    WritableTextType.Word => ushort.Parse(text, NumberStyles.Integer, culture),
                    WritableTextType.Numeric => float.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture),
                    WritableTextType.Integer => int.Parse(text, NumberStyles.Integer, culture),
                    _ => text
                };
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException)
            {
                ShowError($"Invalid value: {ex.Message}");
                return;
            }

            _dirty = false;
            _outstanding++;
            await _writes.WaitAsync();
            var succeeded = false;
            try
            {
                await holder.WriteAsync(value);
                succeeded = true;
            }
            catch (Exception ex)
            {
                _failed = true;
                if (editRevision == _editRevision)
                {
                    _updating = true;
                    try { Text = text; }
                    finally { _updating = false; }
                    if (!ReferenceEquals(holder, Holder))
                        IsReadOnly = true;
                }
                ShowError($"Write failed for '{text}': {ex.Message}. Right-click to discard the failed value.");
                var menu = new ContextMenu();
                var discard = new MenuItem { Header = "Discard failed value" };
                discard.Click += (_, _) =>
                {
                    _timer.Stop();
                    _dirty = false;
                    _failed = false;
                    IsReadOnly = false;
                    ContextMenu = null;
                    ClearError();
                    ShowValue();
                };
                menu.Items.Add(discard);
                ContextMenu = menu;
            }
            finally
            {
                _outstanding--;
                _writes.Release();
            }
            if (succeeded && _outstanding == 0 && !_dirty && !_failed && !IsKeyboardFocusWithin &&
                ReferenceEquals(holder, Holder) && revision == _revision && Equals(holder.Value, value))
                ShowValue();
        }

        private void ShowError(string message)
        {
            BorderBrush = Brushes.Red;
            ToolTip = message;
        }

        private void ClearError()
        {
            ClearValue(BorderBrushProperty);
            ClearValue(ToolTipProperty);
            ContextMenu = null;
        }
    }
}
