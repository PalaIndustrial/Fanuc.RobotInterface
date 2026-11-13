using System;

namespace Fanuc.RobotInterface;

public class RobotAlarm : IEquatable<RobotAlarm>
{
    public AlarmCategory Category { get; set; }
    public short Number { get; set; }

    public AlarmCategory CauseCategory { get; set; }
    public short CauseNumber { get; set; }

    public AlarmSeverity Severity { get; set; }

    public DateTime Time { get; set; }

    public static RobotAlarm FromBytes(byte[] bytes, int start = 0)
    {
        if (bytes == null)
            return null;
        if (bytes.Length < start + 200)
            throw new ArgumentException("need 200 bytes for alarm", nameof(bytes));

        return new RobotAlarm()
        {
            Category = (AlarmCategory)BitConverter.ToInt16(bytes, start + 0),
            Number = BitConverter.ToInt16(bytes, start + 2),
            CauseCategory = (AlarmCategory)BitConverter.ToInt16(bytes, start + 4),
            CauseNumber = BitConverter.ToInt16(bytes, start + 6),
            Severity = (AlarmSeverity)BitConverter.ToInt16(bytes, start + 8),
            Time = _GetTime(
                BitConverter.ToInt16(bytes, start + 10), // year
                BitConverter.ToInt16(bytes, start + 12), // month
                BitConverter.ToInt16(bytes, start + 14), // day
                BitConverter.ToInt16(bytes, start + 16), // hour
                BitConverter.ToInt16(bytes, start + 18), // minute
                BitConverter.ToInt16(bytes, start + 20)  // second
            ),
        };
    }

    private static DateTime _GetTime(short year, short month, short day, short hour, short minute, short second)
    {
        if (year == 0 && month == 0 && day == 0 && hour == 0 && minute == 0 && second == 0)
            return DateTime.MinValue;
        return new DateTime(year, month, day, hour, minute, second);
    }

    public bool Equals(RobotAlarm other)
    {
        if (other == null)
            return false;
        if (Category != other.Category)
            return false;
        if (Number != other.Number)
            return false;
        if (CauseCategory != other.CauseCategory)
            return false;
        if (CauseNumber != other.CauseNumber)
            return false;
        if (Severity != other.Severity)
            return false;
        if (!Equals(Time, other.Time))
            return false;
        return true;
    }

    public override string ToString()
    {
        var msg = $"{Time:yyyy/MM/dd hh:mm:ss}: {Severity}: {Category}-{Number:000}";
        if (CauseCategory != AlarmCategory.OS && CauseNumber != 0)
            return $"{msg}, caused by {CauseCategory}-{CauseNumber:000}";
        return msg;
    }
}