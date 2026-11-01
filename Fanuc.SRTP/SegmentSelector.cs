namespace Fanuc.SRTP;

public enum SegmentSelector : byte
{
    BIT_I = 0x46,
    BIT_Q = 0x48,
    BIT_T = 0x4a,
    BIT_M = 0x4c,
    BIT_SA = 0x4e,
    BIT_SB = 0x50,
    BIT_SC = 0x52,
    BIT_S = 0x54,
    BIT_G = 0x56,

    BYTE_I = 0x10,
    BYTE_Q = 0x12,
    BYTE_T = 0x14,
    BYTE_M = 0x16,
    BYTE_SA = 0x1a,
    BYTE_SB = 0x1c,
    BYTE_SC = 0x1e,
    BYTE_G = 0x38,

    WORD_R = 0x08,
    WORD_AI = 0x0a,
    WORD_AQ = 0x0c,

    INIT = 0x01,    // unknown selector used in init
}
