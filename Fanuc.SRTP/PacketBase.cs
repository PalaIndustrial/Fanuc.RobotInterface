using System;
using System.Linq;

namespace Fanuc.SRTP;

public abstract class PacketBase
{
    protected static ushort _ToUInt16(byte b1, byte b0) => (ushort)(b1 << 8 | b0);
    protected static uint _ToUInt32(byte b3, byte b2, byte b1, byte b0) => (uint)(b3 << 24 | b2 << 16 | b1 << 8 | b0);
    protected static (byte b1, byte b0) _ToBytes(ushort value) => ((byte)((value >> 8) & 0xff), (byte)(value & 0xff));
    protected static (byte b3, byte b2, byte b1, byte b0) _ToBytes(uint value) => ((byte)(value >> 24 & 0xff), (byte)(value >> 16 & 0xff), (byte)(value >> 8 & 0xff), (byte)(value & 0xff));

    public static int ExtraLengthFromHeaderBuffer(byte[] buffer) => _ToUInt16(buffer[5], buffer[4]);

    public const int HEADER_LENGTH = 56;

    public byte[] Header { get; protected set; }

    public PacketType PacketType
    {
        get => (PacketType)_ToUInt16(Header[1], Header[0]);
        set => (Header[1], Header[0]) = _ToBytes((ushort)value);
    }
    protected byte _SequenceNumber { get => Header[2]; set => Header[2] = value; }
    //[FieldOffset(3)] public byte Unknown_3; // always 0, probably reserved
    public virtual ushort ExtraLength { get => _ToUInt16(Header[5], Header[4]); protected set => (Header[5], Header[4]) = _ToBytes(value); }

    //[FieldOffset(6)] public ushort Unknown_6; // always 0
    public uint Unknown_8
    {
        get => _ToUInt32(Header[11], Header[10], Header[9], Header[8]);
        set => (Header[11], Header[10], Header[9], Header[8]) = _ToBytes(value);
    }
    //[FieldOffset(12)] public uint Unknown_12; // always 0
    public uint Unknown_16
    {
        get => _ToUInt32(Header[19], Header[18], Header[17], Header[16]);
        set => (Header[19], Header[18], Header[17], Header[16]) = _ToBytes(value);
    }
    //[FieldOffset(20)] public uint Unknown_20; // always 0
    //[FieldOffset(24)] public ushort Unknown_24; // always 0

    public byte Second { get => Header[26]; set => Header[26] = value; }
    public byte Minute { get => Header[27]; set => Header[27] = value; }
    public byte Hour { get => Header[28]; set => Header[28] = value; }

    //[FieldOffset(29)] public byte Unknown_29; // always 0, probably reserved

    protected byte _SequenceNumber2 { get => Header[30]; set => Header[30] = value; }
    public MessageType MessageType { get => (MessageType)Header[31]; set => Header[31] = (byte)value; }
    public uint MailboxSource
    {
        get => _ToUInt32(Header[35], Header[34], Header[33], Header[32]);
        set => (Header[35], Header[34], Header[33], Header[32]) = _ToBytes(value);
    }
    public uint MailboxDestination
    {
        get => _ToUInt32(Header[39], Header[38], Header[37], Header[36]);
        set => (Header[39], Header[38], Header[37], Header[36]) = _ToBytes(value);
    }
    public virtual byte PacketNumber { get => Header[40]; set => Header[40] = value; }
    public virtual byte TotalPacketNumber { get => Header[41]; set => Header[41] = value; }

    public byte SequenceNumber { get => _SequenceNumber; set => _SequenceNumber = _SequenceNumber2 = value; }

    private byte[] _ExtraPayload;
    public virtual byte[] ExtraPayload
    {
        get => _ExtraPayload;
        set
        {
            _ExtraPayload = value ?? Array.Empty<byte>();
            ExtraLength = (ushort)(value?.Length ?? 0);
        }
    }

    public abstract byte[] Payload { get; set; }
    public abstract byte[] ActualPayload { get; }

    public PacketBase(byte[] buffer = null)
    {
        Header = buffer ?? (new byte[56]);
    }

    public byte[] GetBytes() => Header.Concat(ExtraPayload).ToArray();
}