namespace Fanuc.RobotInterface;

public enum CartisianConfig : byte
{
    Unknown = 0xff,

    Flip = 0b00000001,
    Left = 0b00000010,
    Up = 0b00000100,
    Front = 0b00001000,
    None = 0b00000000,

    NRDB = 0b00000000,
    FRDB = 0b00000001,
    NLDB = 0b00000010,
    FLDB = 0b00000011,
    NRUB = 0b00000100,
    FRUB = 0b00000101,
    NLUB = 0b00000110,
    FLUB = 0b00000111,
    NRDT = 0b00001000,
    FRDT = 0b00001001,
    NLDT = 0b00001010,
    FLDT = 0b00001011,
    NRUT = 0b00001100,
    FRUT = 0b00001101,
    NLUT = 0b00001110,
    FLUT = 0b00001111,
}
