using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using FANUCRobotTest.UI;
using Fanuc.RobotInterface;

namespace FANUCRobotTest.Tests;

[TestClass]
public sealed class RobotDisplayBindingTests
{
    [STATestMethod]
    public void ReplacingRobotRebindsCollectionsAndRestoresSelections()
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            VerifyReplacement();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    private static void VerifyReplacement()
    {
        var settings = new RobotDisplayExtended.SettingsT(Path.GetFullPath($"unused-{Guid.NewGuid():N}.json"));
        settings.SDI.Add(1);
        settings.R.Add(2);
        settings.SysVarI.Add("$TEST");
        settings.POS.Add(10);
        settings.PRG.Add(1);
        settings.ALM.Add(1);
        var first = new ExRobotIF(new RobotIF("first"));
        settings.RestoreSelections(first);
        var source = new ContentControl { Tag = first };
        var items = new ItemsControl();
        BindingOperations.SetBinding(items, ItemsControl.ItemsSourceProperty, new Binding("Tag.SDI") { Source = source });

        var index = 1;
        while (first.SDI.ContainsKey(index))
            index++;
        var oldHolder = first.SDI.GetOrAdd(index);
        Pump();
        Assert.AreSame(first.SDI, items.ItemsSource);
        Assert.AreEqual(first.SDI.Count, items.Items.Count);

        var second = new ExRobotIF(new RobotIF("second", 60009, 8080));
        settings.RestoreSelections(second);
        source.Tag = second;
        Assert.AreSame(second.SDI, items.ItemsSource);
        Assert.AreEqual(second.SDI.Count, items.Items.Count);
        Assert.IsTrue(second.SDI.ContainsKey(1));
        Assert.IsTrue(second.R.ContainsKey(2));
        Assert.IsTrue(second.SysVarI.ContainsKey("$TEST"));
        Assert.IsTrue(second.POS.ContainsKey(10));
        Assert.IsTrue(second.PRG.ContainsKey(1));
        Assert.IsTrue(second.ALM.ContainsKey(1));

        var count = items.Items.Count;
        first.SDI.GetOrAdd(index + 1);
        Assert.AreEqual(count, items.Items.Count);
        var newHolder = second.SDI.GetOrAdd(index);
        Pump();
        Assert.AreNotSame(oldHolder, newHolder);
        Assert.AreEqual(count + 1, items.Items.Count);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
