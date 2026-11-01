namespace Fanuc.SRTP;

public enum MessageType : byte
{
    SHORT = 0xc0,
    SHORT_ACK = 0xd4,
    LONG = 0x80,
    LONG_ACK = 0x94,

    SHORT_FAILED = 0xd1,
}