namespace Fanuc.SRTP;

public abstract class ExtendedPacketBase : PacketBase
{
    public override byte PacketNumber { get => base.PacketNumber; set => Header[48] = base.PacketNumber = value; }
    public override byte TotalPacketNumber { get => base.TotalPacketNumber; set => Header[49] = base.TotalPacketNumber = value; }

    public ServiceRequestCode ServiceRequestCode { get => (ServiceRequestCode)Header[50]; set => Header[50] = (byte)value; }
    public SegmentSelector SegmentSelector { get => (SegmentSelector)Header[51]; set => Header[51] = (byte)value; }

    public override byte[] Payload { get => ExtraPayload; set => ExtraPayload = value; }

    public override byte[] ActualPayload => ExtraPayload;

    public ExtendedPacketBase()
    {
        Unknown_8 = 0x0200;
        Unknown_16 = 0x0200;
    }
}