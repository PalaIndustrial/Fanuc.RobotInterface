using System;

namespace Fanuc.RobotInterface;

public class PositionCartisian : IPosition, IEquatable<PositionCartisian>
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
    public float P { get; set; }
    public float R { get; set; }
    public float E1 { get; set; }
    public float E2 { get; set; }
    public float E3 { get; set; }

    public CartisianConfig Config { get; set; } = CartisianConfig.Unknown;

    public short T4 { get; set; }
    public short T5 { get; set; }
    public short T6 { get; set; }

    public bool Equals(PositionCartisian other)
    {
        if (other == null)
            return false;
        if (X != other.X) return false;
        if (Y != other.Y) return false;
        if (Z != other.Z) return false;
        if (W != other.W) return false;
        if (P != other.P) return false;
        if (R != other.R) return false;
        if (E1 != other.E1) return false;
        if (E2 != other.E2) return false;
        if (E3 != other.E3) return false;
        if (Config != other.Config) return false;
        if (T4 != other.T4) return false;
        if (T5 != other.T5) return false;
        if (T6 != other.T6) return false;
        return true;
    }

    public override string ToString() =>
        $"({X:0.00}, {Y:0.00}, {Z:0.00}, {W:0.00}, {P:0.00}, {R:0.00}) [{E1:0.00}, {E2:0.00}, {E3:0.00}] {{{Config}, {T4}, {T5}, {T6}}}";
}
