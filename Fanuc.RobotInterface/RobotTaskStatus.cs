using System;
using System.Text;

namespace Fanuc.RobotInterface;

public class RobotTaskStatus : IEquatable<RobotTaskStatus>
{
    public string ProgramName { get; set; }
    public short LineNumber { get; set; }
    public RobotTaskState State { get; set; }
    public string Caller { get; set; }

    public bool Equals(RobotTaskStatus other)
    {
        if (other == null)
            return false;
        if (LineNumber != other.LineNumber)
            return false;
        if (State != other.State)
            return false;
        if (!string.Equals(ProgramName, other.ProgramName))
            return false;
        if (!string.Equals(Caller, other.Caller))
            return false;
        return true;
    }

    public static RobotTaskStatus FromBytes(byte[] bytes, int start = 0)
    {
        if (bytes == null)
            return null;
        if (bytes.Length < start + 36)
            throw new ArgumentException("need 36 bytes for task state", nameof(bytes));

        return new RobotTaskStatus()
        {
            ProgramName = Encoding.ASCII.GetString(bytes, start + 0, 16),
            LineNumber = BitConverter.ToInt16(bytes, start + 16),
            State = (RobotTaskState)BitConverter.ToInt16(bytes, start + 18),
            Caller = Encoding.ASCII.GetString(bytes, start + 20, 16),
        };
    }
}