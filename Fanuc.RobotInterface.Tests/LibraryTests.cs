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
    [DataRow(true)]
    [DataRow(false)]
    public void ExtendedRequestSeparatesCountFromPayloadLength(bool payloadFirst)
    {
        var packet = new ExtendedRequestPacket();
        var payload = new byte[] { 1, 2, 3, 4 };
        if (payloadFirst)
        {
            packet.ExtraPayload = payload;
            packet.Count = 2;
        }
        else
        {
            packet.Count = 2;
            packet.ExtraPayload = payload;
        }

        var bytes = packet.GetBytes();
        Assert.AreEqual((ushort)2, packet.Count);
        Assert.AreEqual(payload.Length, PacketBase.ExtraLengthFromHeaderBuffer(bytes));
        Assert.AreEqual((byte)2, bytes[54]);
        Assert.AreEqual((byte)0, bytes[55]);
        CollectionAssert.AreEqual(payload, bytes.Skip(PacketBase.HEADER_LENGTH).ToArray());
    }

    [TestMethod]
    public void ExtraPayloadRejectsOverflowWithoutChangingPacket()
    {
        var packet = new ExtendedRequestPacket { Count = 2 };
        Assert.HasCount(PacketBase.HEADER_LENGTH, packet.GetBytes());
        var maximum = new byte[ushort.MaxValue];
        packet.ExtraPayload = maximum;
        Assert.AreEqual((int)ushort.MaxValue, PacketBase.ExtraLengthFromHeaderBuffer(packet.GetBytes()));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => packet.ExtraPayload = new byte[ushort.MaxValue + 1]);
        Assert.AreSame(maximum, packet.ExtraPayload);
        Assert.AreEqual((ushort)2, packet.Count);
        Assert.AreEqual((int)ushort.MaxValue, PacketBase.ExtraLengthFromHeaderBuffer(packet.GetBytes()));

        packet.ExtraPayload = null!;
        Assert.HasCount(PacketBase.HEADER_LENGTH, packet.GetBytes());
        Assert.AreEqual(0, PacketBase.ExtraLengthFromHeaderBuffer(packet.Header));
    }

    [TestMethod]
    public void PositionParserRejectsIncompleteData()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PositionConverter.ToPosition(new byte[99]));
    }

    [TestMethod]
    public void ExtendedSignalDictionaryCreatesAndReusesHolders()
    {
        var robot = new ExRobotIF(new RobotIF("localhost"));

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

    [TestMethod]
    public void ConcurrentHolderCreationReusesOneInstance()
    {
        var holders = new ExRobotIF(new RobotIF("localhost")).SDI;
        var results = new object[64];

        Parallel.For(0, results.Length, i => results[i] = holders.GetOrAdd(1));

        foreach (var holder in results)
            Assert.AreSame(results[0], holder);
    }

    [TestMethod]
    public void HolderLookupsTolerateConcurrentRemovalAndClear()
    {
        var holders = new ExRobotIF(new RobotIF("localhost")).SDI;
        holders.GetOrAdd(1);

        Parallel.Invoke(
            () =>
            {
                for (var i = 0; i < 20000; i++)
                    Assert.AreEqual(1, holders.GetOrAdd(1).Index);
            },
            () =>
            {
                for (var i = 0; i < 20000; i++)
                {
                    if (i % 2 == 0)
                        holders.Remove(1);
                    else
                        holders.Clear();
                }
            });
    }

    [TestMethod]
    public void ExtendedInterfaceRequiresRobotIF()
    {
        var constructors = typeof(ExRobotIF).GetConstructors();
        Assert.HasCount(2, constructors);
        Assert.IsNotNull(typeof(ExRobotIF).GetConstructor(new[] { typeof(RobotIF) }));
        Assert.IsNotNull(typeof(ExRobotIF).GetConstructor(new[] { typeof(RobotIF), typeof(int) }));

        var robot = new ExRobotIF(new RobotIF("localhost"));
        Assert.IsFalse(robot.IsConnected);
        Assert.AreEqual("localhost", robot.Host);
        Assert.ThrowsExactly<ArgumentNullException>(() => new ExRobotIF(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ExRobotIF(null!, 20));
    }

    [TestMethod]
    [DataRow(int.MinValue, 10)]
    [DataRow(-1, 10)]
    [DataRow(0, 10)]
    [DataRow(9, 10)]
    [DataRow(10, 10)]
    [DataRow(50, 50)]
    [DataRow(int.MaxValue, int.MaxValue)]
    public void ExtendedInterfaceClampsPollingDelay(int requested, int expected)
    {
        Assert.AreEqual(10, new ExRobotIF(new RobotIF("localhost")).PollingDelay);
        Assert.AreEqual(expected, new ExRobotIF(new RobotIF("localhost"), requested).PollingDelay);
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
