namespace Fanuc.RobotInterface;

public interface IRobotIF
{
    bool IsConnected { get; }
    string Host { get; }
    ushort SrtpPort { get; }
    ushort HttpPort { get; }

    /// <summary>
    /// Connects using an approximate TCP connect timeout and per-send/receive
    /// timeout in milliseconds. Zero disables these timeouts. This is not an
    /// overall handshake deadline; hostname resolution may continue after a
    /// timeout on older targets.
    /// </summary>
    void Connect(int timeout = RobotIF.DEFAULT_TIMEOUT);
    void Disconnect();

    void ClearAlarm();
    string ExecuteKCL(string command);
    string ExecuteKarel(string program);

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