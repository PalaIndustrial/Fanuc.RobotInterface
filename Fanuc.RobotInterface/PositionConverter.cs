using System;

namespace Fanuc.RobotInterface;

public static class PositionConverter
{
    public static Position ToPosition(byte[] bytes, int start = 0)
    {
        if (bytes == null)
            return null;
        if (bytes.Length < start + 100)
            throw new ArgumentException($"need 100 bytes to convert a position, got only {bytes.Length - start}", nameof(bytes));

        return new Position()
        {
            UserFrame = BitConverter.ToInt16(bytes, start + 90),
            UserTool = BitConverter.ToInt16(bytes, start + 92),
            Cartisian = BitConverter.ToInt16(bytes, start + 50) == 0 ?
                null :
                new PositionCartisian()
                {
                    X = BitConverter.ToSingle(bytes, start + 0),
                    Y = BitConverter.ToSingle(bytes, start + 4),
                    Z = BitConverter.ToSingle(bytes, start + 8),
                    W = BitConverter.ToSingle(bytes, start + 12),
                    P = BitConverter.ToSingle(bytes, start + 16),
                    R = BitConverter.ToSingle(bytes, start + 20),
                    E1 = BitConverter.ToSingle(bytes, start + 24),
                    E2 = BitConverter.ToSingle(bytes, start + 28),
                    E3 = BitConverter.ToSingle(bytes, start + 32),

                    T4 = BitConverter.ToInt16(bytes, start + 44),
                    T5 = BitConverter.ToInt16(bytes, start + 46),
                    T6 = BitConverter.ToInt16(bytes, start + 48),

                    Config =
                        (BitConverter.ToUInt16(bytes, start + 36) != 0 ? CartisianConfig.Flip : CartisianConfig.None) |
                        (BitConverter.ToUInt16(bytes, start + 38) != 0 ? CartisianConfig.Left : CartisianConfig.None) |
                        (BitConverter.ToUInt16(bytes, start + 40) != 0 ? CartisianConfig.Up : CartisianConfig.None) |
                        (BitConverter.ToUInt16(bytes, start + 42) != 0 ? CartisianConfig.Front : CartisianConfig.None),
                },
            Joint = BitConverter.ToInt16(bytes, start + 88) == 0 ?
                null :
                new PositionJoint()
                {
                    J1 = BitConverter.ToSingle(bytes, start + 52),
                    J2 = BitConverter.ToSingle(bytes, start + 56),
                    J3 = BitConverter.ToSingle(bytes, start + 60),
                    J4 = BitConverter.ToSingle(bytes, start + 64),
                    J5 = BitConverter.ToSingle(bytes, start + 68),
                    J6 = BitConverter.ToSingle(bytes, start + 72),
                    J7 = BitConverter.ToSingle(bytes, start + 76),
                    J8 = BitConverter.ToSingle(bytes, start + 80),
                    J9 = BitConverter.ToSingle(bytes, start + 84),
                },
        };
    }

    public static byte[] GetBytes(Position pos)
    {
        if (pos.Cartisian != null)
        {
            var bytes = new byte[52];
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.X), 0, bytes, 0, 4);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.Y), 0, bytes, 4, 4);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.Z), 0, bytes, 8, 4);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.W), 0, bytes, 12, 4);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.P), 0, bytes, 16, 4);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.R), 0, bytes, 20, 4);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.E1), 0, bytes, 24, 4);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.E2), 0, bytes, 28, 4);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.E3), 0, bytes, 32, 4);
            Array.Copy(BitConverter.GetBytes((short)(pos.Cartisian.Config.HasFlag(CartisianConfig.Flip) ? 0x0001 : 0x0000)), 0, bytes, 36, 2);
            Array.Copy(BitConverter.GetBytes((short)(pos.Cartisian.Config.HasFlag(CartisianConfig.Left) ? 0x0001 : 0x0000)), 0, bytes, 38, 2);
            Array.Copy(BitConverter.GetBytes((short)(pos.Cartisian.Config.HasFlag(CartisianConfig.Up) ? 0x0001 : 0x0000)), 0, bytes, 40, 2);
            Array.Copy(BitConverter.GetBytes((short)(pos.Cartisian.Config.HasFlag(CartisianConfig.Front) ? 0x0001 : 0x0000)), 0, bytes, 42, 2);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.T4), 0, bytes, 44, 2);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.T5), 0, bytes, 46, 2);
            Array.Copy(BitConverter.GetBytes(pos.Cartisian.T6), 0, bytes, 48, 2);
            //Array.Copy(BitConverter.GetBytes((short)0x0001), 0, bytes, 50, 2);
            return bytes;
        }
        if (pos.Joint != null)
        {
            var bytes = new byte[38];
            Array.Copy(BitConverter.GetBytes(pos.Joint.J1), 0, bytes, 0, 4);
            Array.Copy(BitConverter.GetBytes(pos.Joint.J2), 0, bytes, 4, 4);
            Array.Copy(BitConverter.GetBytes(pos.Joint.J3), 0, bytes, 8, 4);
            Array.Copy(BitConverter.GetBytes(pos.Joint.J4), 0, bytes, 12, 4);
            Array.Copy(BitConverter.GetBytes(pos.Joint.J5), 0, bytes, 16, 4);
            Array.Copy(BitConverter.GetBytes(pos.Joint.J6), 0, bytes, 20, 4);
            Array.Copy(BitConverter.GetBytes(pos.Joint.J7), 0, bytes, 24, 4);
            Array.Copy(BitConverter.GetBytes(pos.Joint.J8), 0, bytes, 28, 4);
            Array.Copy(BitConverter.GetBytes(pos.Joint.J9), 0, bytes, 32, 4);
            //Array.Copy(BitConverter.GetBytes((short)0x0001), 0, bytes, 36, 2);
            return bytes;
        }
        throw new NotImplementedException();
    }
}