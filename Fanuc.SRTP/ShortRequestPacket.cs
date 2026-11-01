namespace Fanuc.SRTP;

public class ShortRequestPacket : ShortPacketBase, IRequestPacket
{
    public ushort Index { get => _ToUInt16(Header[45], Header[44]); set => (Header[45], Header[44]) = _ToBytes(value); }
    public ushort Count { get => _ToUInt16(Header[47], Header[46]); set => (Header[47], Header[46]) = _ToBytes(value); }

    public override byte[] Payload
    {
        get => new byte[] { Header[48], Header[49], Header[50], Header[51], Header[52], Header[53] };
        set
        {
            if (value.Length > 0) Header[48] = value[0];
            if (value.Length > 1) Header[49] = value[1];
            if (value.Length > 2) Header[50] = value[2];
            if (value.Length > 3) Header[51] = value[3];
            if (value.Length > 4) Header[52] = value[4];
            if (value.Length > 5) Header[53] = value[5];
        }
    }

    //[FieldOffset(54)] public ushort Unknown_54;

    public override byte[] ActualPayload => Payload;

    public ShortRequestPacket()
    {
        PacketType = PacketType.REQUEST;
        MessageType = MessageType.SHORT;
    }
}