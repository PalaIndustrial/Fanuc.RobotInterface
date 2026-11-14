using Fanuc.RobotInterface.Collections;
using System;
using System.Threading.Tasks;

namespace Fanuc.RobotInterface
{
    public interface IExRobotIF : IRobotIF
    {
        Exception LastError { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> SDI { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> SDO { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> RDI { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> RDO { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> UI { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> UO { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> SI { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> SO { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> WI { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> WO { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> WSI { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> PMC_K { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<bool>> PMC_R { get; }

        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<ushort>> GI { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<ushort>> GO { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<ushort>> AI { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<ushort>> AO { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotSignalHolderBase<ushort>> PMC_D { get; }

        IRobotDataDictionary<int, ExRobotIF.RobotWritableDataHolderBase<float>> R { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotWritableDataHolderBase<Position>> PR { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotWritableDataHolderBase<string>> SR { get; }

        IRobotDataDictionary<string, ExRobotIF.RobotWritableDataHolderBase<int>> SysVarI { get; }
        IRobotDataDictionary<string, ExRobotIF.RobotWritableDataHolderBase<Position>> SysVarP { get; }
        IRobotDataDictionary<string, ExRobotIF.RobotWritableDataHolderBase<string>> SysVarS { get; }

        IRobotDataDictionary<uint, ExRobotIF.RobotDataHolderBase<Position>> POS { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotDataHolderBase<RobotTaskStatus>> PRG { get; }
        IRobotDataDictionary<int, ExRobotIF.RobotDataHolderBase<RobotAlarm>> ALM { get; }

        /// <summary>
        /// Connects using an approximate TCP connect timeout and per-send/receive
        /// timeout in milliseconds. Zero disables these timeouts. This is not an
        /// overall handshake deadline; hostname resolution may continue after a
        /// timeout on older targets.
        /// </summary>
        Task ConnectAsync(string host, ushort port, int timeout);

        Task<bool[]> AsyncReadDI(int index, ushort count);
        Task<bool[]> AsyncReadDO(int index, ushort count);
        Task<ushort[]> AsyncReadGI(int index, ushort count);
        Task<ushort[]> AsyncReadGO(int index, ushort count);
        Task AsyncWriteDI(int index, params bool[] values);
        Task AsyncWriteDO(int index, params bool[] values);
        Task AsyncWriteGI(int index, params ushort[] values);
        Task AsyncWriteGO(int index, params ushort[] values);

        Task<byte[]> AsyncReadSNPX(int index, ushort count);
        Task AsyncWriteSNPX(int index, params byte[] values);
        Task AsyncWriteCommand(string command);

        Task<int> AsyncWriteAssignment(int length, string target);
    }
}
