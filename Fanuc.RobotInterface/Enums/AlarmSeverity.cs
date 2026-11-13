namespace Fanuc.RobotInterface;

public enum AlarmSeverity : short
{
    NONE = 128,
    WARN = 0,
    PAUSE_L = 2,
    PAUSE_G = 34,
    STOP_L = 6,
    STOP_G = 38,
    SERVO = 54,
    ABORT_L = 11,
    ABORT_G = 45,
    SERVO2 = 58,
    SYSTEM = 122,
}
