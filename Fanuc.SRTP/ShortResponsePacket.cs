using System.Linq;

namespace Fanuc.SRTP;

public class ShortResponsePacket : ShortPacketBase, IResponsePacket
{
    //[FieldOffset(42)] public ushort Unknown_42;

    public override byte[] Payload
    {
        get => [Header[44], Header[45], Header[46], Header[47], Header[48], Header[49]];
        set
        {
            if (value.Length > 0) Header[44] = value[0];
            if (value.Length > 1) Header[45] = value[1];
            if (value.Length > 2) Header[46] = value[2];
            if (value.Length > 3) Header[47] = value[3];
            if (value.Length > 4) Header[48] = value[4];
            if (value.Length > 5) Header[49] = value[5];
        }
    }

    //[FieldOffset(50)] public uint Unknown_50;
    //[FieldOffset(54)] public ushort Unknown_54;

    public ShortResponsePacket(byte[] buffer)
    {
        Header = buffer.Take(56).ToArray();
    }
}