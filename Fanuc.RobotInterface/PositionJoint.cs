using System;

namespace Fanuc.RobotInterface;

public class PositionJoint : IPosition, IEquatable<PositionJoint>
{
    public float J1 { get; set; }
    public float J2 { get; set; }
    public float J3 { get; set; }
    public float J4 { get; set; }
    public float J5 { get; set; }
    public float J6 { get; set; }

    public float J7 { get; set; }
    public float J8 { get; set; }
    public float J9 { get; set; }

    public bool Equals(PositionJoint other)
    {
        if (other == null)
            return false;
        if (J1 != other.J1) return false;
        if (J2 != other.J2) return false;
        if (J3 != other.J3) return false;
        if (J4 != other.J4) return false;
        if (J5 != other.J5) return false;
        if (J6 != other.J6) return false;
        if (J7 != other.J7) return false;
        if (J8 != other.J8) return false;
        if (J9 != other.J9) return false;
        return true;
    }

    public override string ToString() =>
        $"({J1:0.00}, {J2:0.00}, {J3:0.00}, {J4:0.00}, {J5:0.00}, {J6:0.00}) [{J7:0.00}, {J8:0.00}, {J9:0.00}]";
}
