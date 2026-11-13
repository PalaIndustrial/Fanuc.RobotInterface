namespace Fanuc.RobotInterface;

public interface IRobotIF
{
    bool IsConnected { get; }
    string Host { get; }

    void Connect(string host, ushort port, int timeout);
    void Disconnect();

    void ClearAlarm();

    #region IO
    bool[] ReadDI(int index, ushort count);
    bool[] ReadDO(int index, ushort count);
    void WriteDI(int index, params bool[] values);
    void WriteDO(int index, params bool[] values);

    ushort[] ReadGI(int index, ushort count);
    ushort[] ReadGO(int index, ushort count);
    void WriteGI(int index, params ushort[] values);
    void WriteGO(int index, params ushort[] values);
    #endregion

    #region DataTable
    void WriteCommand(string command);
    void WriteSNPX(int index, params byte[] values);
    byte[] ReadSNPX(int index, ushort count);
    #endregion
}