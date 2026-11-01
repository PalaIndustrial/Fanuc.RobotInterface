using System;

namespace Fanuc.SRTP;

public abstract class ShortPacketBase : PacketBase
{
    public ServiceRequestCode ServiceRequestCode { get => (ServiceRequestCode)Header[42]; set => Header[42] = (byte)value; }
    public SegmentSelector SegmentSelector { get => (SegmentSelector)Header[43]; set => Header[43] = (byte)value; }

    public override byte[] ActualPayload => Payload;

    public ShortPacketBase()
    {
        Unknown_8 = 0x0100;
        Unknown_16 = 0x0100;

        ExtraPayload = Array.Empty<byte>();
    }
}