using Fanuc.RobotInterface.Collections;
using System;
using System.Linq;
using System.Text;

namespace Fanuc.RobotInterface
{
    public partial class ExRobotIF
    {

        private abstract class RobotSystemVariableHolderBase<T> : RobotWritableComplexDataHolderBase<T>
        {
            public string Key { get; }

            public RobotSystemVariableHolderBase(IExRobotIF robot, string key) :
                base(robot)
            {
                Key = key;
                if (robot.IsConnected)
                    WriteAssignment();
            }
        }

        private class RobotIntegerSystemVariableHolder : RobotSystemVariableHolderBase<int>
        {
            public RobotIntegerSystemVariableHolder(IExRobotIF robot, string key) :
                base(robot, key)
            {
            }

            public override void WriteAssignment() => Offset = Robot.WriteAssignment(2, $"{Key} 1");

            protected override int ReadValue()
            {
                var bytes = Robot.ReadSNPX(Offset, 4);
                return bytes[3] << 24 | bytes[2] << 16 | bytes[1] << 8 | bytes[0];
            }

            protected override void WriteValue(int value) =>
                Robot.WriteSNPX(Offset, (byte)(value & 0xff), (byte)((value >> 8) & 0xff), (byte)((value >> 16) & 0xff), (byte)((value >> 24) & 0xff));
        }

        private class RobotStringSystemVariableHolder : RobotSystemVariableHolderBase<string>
        {
            public RobotStringSystemVariableHolder(IExRobotIF robot, string key) :
                base(robot, key)
            {
            }

            public override void WriteAssignment() => Offset = Robot.WriteAssignment(40, $"{Key} 1");

            protected override string ReadValue() => Offset == -1 ? null : Encoding.ASCII.GetString(Robot.ReadSNPX(Offset, 80));

            protected override void WriteValue(string value)
            {
                if (Offset == -1)
                    return;
                var bytes = Encoding.ASCII.GetBytes(value);
                if (bytes.Length % 2 == 1)
                    bytes = bytes.Concat(new byte[1]).ToArray();
                Robot.WriteSNPX(Offset, bytes.Take(80).ToArray());
            }
        }

        private class RobotPositionSystemVariableHolder : RobotSystemVariableHolderBase<Position>
        {
            public RobotPositionSystemVariableHolder(IExRobotIF robot, string key) :
                base(robot, key)
            {
            }

            public override void WriteAssignment() => Offset = Robot.WriteAssignment(50, $"{Key} 0.0");

            protected override void WriteValue(Position value)
            {
                if (value.Cartisian != null)
                {
                    Robot.WriteSNPX(Offset, PositionConverter.GetBytes(value));
                }
                else if (value.Joint != null)
                {
                    Robot.WriteSNPX(Offset + 26, PositionConverter.GetBytes(value));
                }
                else
                    throw new NotImplementedException();
            }

            protected override Position ReadValue() => Offset == -1 ? null : PositionConverter.ToPosition(Robot.ReadSNPX(Offset, 100));
        }

        public IRobotDataDictionary<string, RobotWritableDataHolderBase<int>> SysVarI { get; }
        public IRobotDataDictionary<string, RobotWritableDataHolderBase<Position>> SysVarP { get; }
        public IRobotDataDictionary<string, RobotWritableDataHolderBase<string>> SysVarS { get; }
    }
}
