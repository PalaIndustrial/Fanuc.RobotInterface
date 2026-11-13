using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FANUCRobotTest.UI;
using Fanuc.RobotInterface;

namespace FANUCRobotTest.Tests;

[TestClass]
public sealed class WritableTextBoxTests
{
    [STATestMethod]
    public void EditsAreDebouncedAndWrittenAsTypedValues()
    {
        var holder = new Holder((ushort)12);
        var box = new WritableTextBox { ValueType = WritableTextType.Word, Holder = holder };
        Assert.AreEqual("12", box.Text);

        box.Text = "2";
        box.Text = "234";
        Assert.IsEmpty(holder.Writes);
        Pump(TimeSpan.FromMilliseconds(700));

        CollectionAssert.AreEqual(new object[] { (ushort)234 }, holder.Writes);
        holder.Update((ushort)345);
        Assert.AreEqual("345", box.Text);
        Assert.HasCount(1, holder.Writes);
    }

    [STATestMethod]
    public void InvalidInputIsNotWrittenAndCanBeCorrected()
    {
        var holder = new Holder(5);
        var box = new WritableTextBox { ValueType = WritableTextType.Integer, Holder = holder };
        box.Text = "not a number";
        Pump(TimeSpan.FromMilliseconds(700));

        Assert.IsEmpty(holder.Writes);
        Assert.AreEqual(Brushes.Red, box.BorderBrush);
        StringAssert.Contains((string)box.ToolTip, "Invalid value");

        box.Text = "-17";
        Pump(TimeSpan.FromMilliseconds(700));
        CollectionAssert.AreEqual(new object[] { -17 }, holder.Writes);
        Assert.IsNull(box.ToolTip);
    }

    [STATestMethod]
    public void EditsDuringAWriteAreSentInOrder()
    {
        var firstWrite = new TaskCompletionSource();
        var holder = new Holder("old") { OnWrite = _ => firstWrite.Task };
        var box = new WritableTextBox { ValueType = WritableTextType.String, Holder = holder };
        box.Text = "first";
        Pump(TimeSpan.FromMilliseconds(700));
        box.Text = "second";
        Pump(TimeSpan.FromMilliseconds(700));
        CollectionAssert.AreEqual(new object[] { "first" }, holder.Writes);

        holder.OnWrite = _ => Task.CompletedTask;
        firstWrite.SetResult();
        Pump(TimeSpan.FromMilliseconds(100));
        CollectionAssert.AreEqual(new object[] { "first", "second" }, holder.Writes);
    }

    [STATestMethod]
    public void FailedWriteIsNotRetriedByFocusUnloadOrHolderChange()
    {
        var holder = new Holder("old") { OnWrite = _ => Task.FromException(new InvalidOperationException("uncertain")) };
        var box = new WritableTextBox { ValueType = WritableTextType.String, Holder = holder };
        box.Text = "attempted";
        Pump(TimeSpan.FromMilliseconds(700));

        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, null)
        {
            RoutedEvent = Keyboard.LostKeyboardFocusEvent
        });
        box.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        holder.Update("remote");
        var replacement = new Holder("replacement");
        box.Holder = replacement;
        Pump(TimeSpan.FromMilliseconds(700));

        CollectionAssert.AreEqual(new object[] { "attempted" }, holder.Writes);
        Assert.IsEmpty(replacement.Writes);
        Assert.AreEqual("attempted", box.Text);
        Assert.IsTrue(box.IsReadOnly);
        Assert.AreEqual(Brushes.Red, box.BorderBrush);
        StringAssert.Contains((string)box.ToolTip, "uncertain");
        var discard = (MenuItem)box.ContextMenu.Items[0];
        discard.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.IsFalse(box.IsReadOnly);
        Assert.AreEqual("replacement", box.Text);
        Assert.IsNull(box.ToolTip);
        Assert.IsEmpty(replacement.Writes);
    }

    [STATestMethod]
    public void FailedWriteRequiresNewEditToWriteAgain()
    {
        var holder = new Holder("old") { OnWrite = _ => Task.FromException(new InvalidOperationException("uncertain")) };
        var box = new WritableTextBox { ValueType = WritableTextType.String, Holder = holder };
        box.Text = "first";
        Pump(TimeSpan.FromMilliseconds(700));
        holder.OnWrite = _ => Task.CompletedTask;
        box.Text = "second";
        Pump(TimeSpan.FromMilliseconds(700));
        CollectionAssert.AreEqual(new object[] { "first", "second" }, holder.Writes);
    }

    [STATestMethod]
    public void FailedWriteAfterHolderChangesRemainsVisible()
    {
        var completion = new TaskCompletionSource();
        var holder = new Holder("old") { OnWrite = _ => completion.Task };
        var box = new WritableTextBox { ValueType = WritableTextType.String, Holder = holder };
        box.Text = "attempted";
        Pump(TimeSpan.FromMilliseconds(700));
        var replacement = new Holder("replacement");
        box.Holder = replacement;
        completion.SetException(new InvalidOperationException("uncertain"));
        Pump(TimeSpan.FromMilliseconds(100));
        box.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.AreEqual("attempted", box.Text);
        Assert.IsTrue(box.IsReadOnly);
        StringAssert.Contains((string)box.ToolTip, "uncertain");
        CollectionAssert.AreEqual(new object[] { "attempted" }, holder.Writes);
        Assert.IsEmpty(replacement.Writes);
    }

    [STATestMethod]
    public void ValueTypeCanBeSetAfterHolder()
    {
        var holder = new Holder(12.5f);
        var box = new WritableTextBox { Holder = holder, ValueType = WritableTextType.Numeric };
        Assert.AreEqual("12.500", box.Text);
        Assert.IsEmpty(holder.Writes);
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class Holder(object value) : IWritableValueHolder, INotifyPropertyChanged
    {
        public object Value { get; set; } = value;
        public List<object> Writes { get; } = [];
        public Func<object, Task> OnWrite { get; set; } = _ => Task.CompletedTask;
        public event PropertyChangedEventHandler? PropertyChanged;

        public Task WriteAsync(object value)
        {
            Writes.Add(value);
            return OnWrite(value);
        }

        public void Update(object value)
        {
            Value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}
