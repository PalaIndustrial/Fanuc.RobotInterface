using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
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
        var httpPort = (DefaultValueAttribute?)properties[nameof(App.SettingsT.RobotHttpPort)]?
            .Attributes[typeof(DefaultValueAttribute)];
        var timeout = (DefaultValueAttribute?)properties[nameof(App.SettingsT.RobotTimeout)]?
            .Attributes[typeof(DefaultValueAttribute)];

        Assert.IsNotNull(port);
        Assert.IsNotNull(httpPort);
        Assert.IsNotNull(timeout);
        Assert.AreEqual((ushort)RobotIF.DEFAULT_PORT, port.Value);
        Assert.AreEqual((ushort)RobotIF.DEFAULT_HTTP_PORT, httpPort.Value);
        Assert.AreEqual(RobotIF.DEFAULT_TIMEOUT, timeout.Value);
    }

    [TestMethod]
    public void EndpointRequiresHostAndBothPorts()
    {
        var settings = new App.SettingsT(Path.GetFullPath($"unused-{Guid.NewGuid():N}.json"));
        settings.RobotHost = " ";
        Assert.IsFalse(settings.HasValidEndpoint);
        settings.RobotHost = "controller";
        settings.RobotPort = 0;
        Assert.IsFalse(settings.HasValidEndpoint);
        settings.RobotPort = RobotIF.DEFAULT_PORT;
        settings.RobotHttpPort = 0;
        Assert.IsFalse(settings.HasValidEndpoint);
        settings.RobotHttpPort = RobotIF.DEFAULT_HTTP_PORT;
        Assert.IsTrue(settings.HasValidEndpoint);
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

    [STATestMethod]
    public void MissingRobotBindingsBehaveAsDisconnected()
    {
        var source = new ContentControl();
        var input = new TextBox();
        var connect = new Button();
        var disconnect = new Button();
        var alarm = new Button();
        Binding BindingFor(IValueConverter? converter = null, object? fallback = null) => new("Tag.IsChecked")
        {
            Source = source,
            Converter = converter,
            FallbackValue = fallback ?? DependencyProperty.UnsetValue
        };
        BindingOperations.SetBinding(input, UIElement.IsEnabledProperty, BindingFor(new BooleanInvertConverter(), true));
        BindingOperations.SetBinding(connect, UIElement.VisibilityProperty, BindingFor(new BooleanToObjectConverter
        {
            TrueValue = Visibility.Hidden, FalseValue = Visibility.Visible, NullValue = Visibility.Visible
        }, Visibility.Visible));
        BindingOperations.SetBinding(disconnect, UIElement.VisibilityProperty, BindingFor(new BooleanToObjectConverter
        {
            TrueValue = Visibility.Visible, FalseValue = Visibility.Hidden, NullValue = Visibility.Hidden
        }, Visibility.Hidden));
        BindingOperations.SetBinding(alarm, UIElement.IsEnabledProperty, BindingFor(fallback: false));

        Assert.IsTrue(input.IsEnabled);
        Assert.AreEqual(Visibility.Visible, connect.Visibility);
        Assert.AreEqual(Visibility.Hidden, disconnect.Visibility);
        Assert.IsFalse(alarm.IsEnabled);

        var robot = new CheckBox { IsChecked = true };
        source.Tag = robot;
        Assert.IsFalse(input.IsEnabled);
        Assert.AreEqual(Visibility.Hidden, connect.Visibility);
        Assert.AreEqual(Visibility.Visible, disconnect.Visibility);
        Assert.IsTrue(alarm.IsEnabled);

        robot.IsChecked = false;
        Assert.IsTrue(input.IsEnabled);
        Assert.AreEqual(Visibility.Visible, connect.Visibility);
        Assert.AreEqual(Visibility.Hidden, disconnect.Visibility);
        Assert.IsFalse(alarm.IsEnabled);
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
