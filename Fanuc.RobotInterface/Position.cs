using System;
using System.Collections.Generic;

namespace Fanuc.RobotInterface;

public class Position : IPosition, IEquatable<Position>
{
    public short UserFrame { get; set; }
    public short UserTool { get; set; }
    public PositionJoint Joint { get; set; }
    public PositionCartisian Cartisian { get; set; }

    public bool Equals(Position other)
    {
        if (other == null)
            return false;
        if (UserFrame != other.UserFrame)
            return false;
        if (UserTool != other.UserTool)
            return false;
        if (!EqualityComparer<PositionJoint>.Default.Equals(Joint, other.Joint))
            return false;
        if (!EqualityComparer<PositionCartisian>.Default.Equals(Cartisian, other.Cartisian))
            return false;
        return true;
    }
}
