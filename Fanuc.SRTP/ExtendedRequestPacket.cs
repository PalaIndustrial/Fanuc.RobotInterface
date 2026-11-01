namespace Fanuc.SRTP;

public class ExtendedRequestPacket : ExtendedPacketBase, IRequestPacket
{
    public ushort Index { get => _ToUInt16(Header[53], Header[52]); set => (Header[53], Header[52]) = _ToBytes(value); }
    public ushort Count { get => _ToUInt16(Header[55], Header[54]); set => (Header[55], Header[54]) = _ToBytes(base.ExtraLength = value); }

    public ExtendedRequestPacket()
    {
        PacketType = PacketType.REQUEST;
        MessageType = MessageType.LONG;
    }
}