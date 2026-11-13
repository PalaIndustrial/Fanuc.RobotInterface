using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using FANUCRobotTest.Converters;
using FANUCRobotTest.UI;
using Fanuc.RobotInterface;

namespace FANUCRobotTest.Tests;

[TestClass]
public sealed class ConverterTests
{
    [TestMethod]
    public void ConnectionDefaultsMatchRobotInterface()
    {
        var properties = TypeDescriptor.GetProperties(typeof(App.SettingsT));
        var port = (DefaultValueAttribute?)properties[nameof(App.SettingsT.RobotPort)]?
            .Attributes[typeof(DefaultValueAttribute)];
        var timeout = (DefaultValueAttribute?)properties[nameof(App.SettingsT.RobotTimeout)]?
            .Attributes[typeof(DefaultValueAttribute)];

        Assert.IsNotNull(port);
        Assert.IsNotNull(timeout);
        Assert.AreEqual((ushort)RobotIF.DEFAULT_PORT, port.Value);
        Assert.AreEqual(RobotIF.DEFAULT_TIMEOUT, timeout.Value);
    }

    [STATestMethod]
    public void InvertedBooleanBindingUpdatesTarget()
    {
        var source = new CheckBox { IsChecked = true };
        var target = new CheckBox();
        BindingOperations.SetBinding(target, CheckBox.IsCheckedProperty, new Binding(nameof(CheckBox.IsChecked))
        {
            Source = source,
            Converter = new BooleanInvertConverter()
        });

        Assert.IsNotNull(target.IsChecked);
        Assert.IsFalse(target.IsChecked.Value);
        source.IsChecked = false;
        Assert.IsNotNull(target.IsChecked);
        Assert.IsTrue(target.IsChecked.Value);
    }

    [TestMethod]
    [DataRow(RobotTaskType.All, "")]
    [DataRow(RobotTaskType.IgnoreMacro, "M")]
    [DataRow(RobotTaskType.IgnoreKarel, "K")]
    [DataRow(RobotTaskType.IgnoreMacroKarel, "MK")]
    public void TaskTypeConverterReturnsExpectedSymbol(RobotTaskType type, string expected)
    {
        var converter = new RobotTaskTypeToSymbolConverter();
        var result = converter.Convert(type, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.AreEqual(expected, result);
    }
}
