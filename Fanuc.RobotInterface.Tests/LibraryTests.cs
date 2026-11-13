using System.Runtime.Versioning;
using Fanuc.RobotInterface;
using Fanuc.SRTP;

namespace Fanuc.RobotInterface.Tests;

[TestClass]
public sealed class LibraryTests
{
    [TestMethod]
    public void ShortRequestSerializesAddressCountAndPayload()
    {
        var packet = new ShortRequestPacket
        {
            SequenceNumber = 7,
            Index = 0x1234,
            Count = 0x5678,
            Payload = new byte[] { 0xAA, 0xBB }
        };

        var bytes = packet.GetBytes();

        Assert.HasCount(PacketBase.HEADER_LENGTH, bytes);
        Assert.AreEqual((byte)7, bytes[2]);
        Assert.AreEqual((byte)7, bytes[30]);
        Assert.AreEqual((byte)0x34, bytes[44]);
        Assert.AreEqual((byte)0x12, bytes[45]);
        Assert.AreEqual((byte)0x78, bytes[46]);
        Assert.AreEqual((byte)0x56, bytes[47]);
        Assert.AreEqual((byte)0xAA, bytes[48]);
        Assert.AreEqual((byte)0xBB, bytes[49]);
    }

    [TestMethod]
    public void PositionParserRejectsIncompleteData()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PositionConverter.ToPosition(new byte[99]));
    }

    [TestMethod]
    public void ExtendedSignalDictionaryCreatesAndReusesHolders()
    {
        var robot = new ExRobotIF(new RobotIF());

        var first = robot.SDI[1];
        var again = robot.SDI[1];
        var second = robot.SDI[2];

        Assert.AreSame(first, again);
        Assert.AreNotSame(first, second);
#pragma warning disable MSTEST0037 // Check the dictionary's Count property, not just enumeration.
        Assert.AreEqual(2, robot.SDI.Count);
#pragma warning restore MSTEST0037
        Assert.AreEqual(1, first.Index);
        Assert.AreEqual(2, second.Index);
    }

#if NET8_0
    [TestMethod]
    public void Net8ConsumesNetStandardLibraryAssets()
    {
        AssertNetStandard(typeof(PacketBase));
        AssertNetStandard(typeof(RobotIF));
        AssertNetStandard(typeof(ExRobotIF));
    }

    private static void AssertNetStandard(Type type)
    {
        var attribute = (TargetFrameworkAttribute?)Attribute.GetCustomAttribute(
            type.Assembly, typeof(TargetFrameworkAttribute));
        Assert.IsNotNull(attribute);
        Assert.AreEqual(".NETStandard,Version=v2.0", attribute.FrameworkName);
    }
#endif
}
