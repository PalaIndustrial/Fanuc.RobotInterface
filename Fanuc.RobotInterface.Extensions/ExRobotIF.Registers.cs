using Fanuc.RobotInterface.Collections;
using System;
using System.Linq;
using System.Text;

namespace Fanuc.RobotInterface
{
    public partial class ExRobotIF
    {
        public abstract class RobotRegisterHolderBase<T> : RobotWritableComplexDataHolderBase<T>
        {
            public int Index { get; }


            public RobotRegisterHolderBase(IExRobotIF robot, int index) :
                base(robot)
            {
                Index = index;
                if (robot.IsConnected)
                    WriteAssignment();
            }
        }

        private class RobotNumericRegisterHolder : RobotRegisterHolderBase<float>
        {
            public RobotNumericRegisterHolder(IExRobotIF robot, int index) :
                base(robot, index)
            {
            }

            public override void WriteAssignment() => Offset = Robot.WriteAssignment(2, $"R[{Index}] 0");

            protected override void WriteValue(float value)
            {
                if (Offset != -1)
                    Robot.WriteSNPX(Offset, BitConverter.GetBytes(value));
            }

            protected override float ReadValue() => Offset == -1 ? default : BitConverter.ToSingle(Robot.ReadSNPX(Offset, 4), 0);
        }

        private class RobotPositionRegisterHolder : RobotRegisterHolderBase<Position>
        {
            public RobotPositionRegisterHolder(IExRobotIF robot, int index) :
                base(robot, index)
            {
            }

            public override void WriteAssignment() => Offset = Robot.WriteAssignment(50, $"PR[{Index}] 0.0");

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

        private class RobotStringRegisterHolder : RobotRegisterHolderBase<string>
        {
            public override string Value { get => base.Value; set => base.Value = value.Length > 80 ? value.Substring(0, 80) : value; }

            public RobotStringRegisterHolder(IExRobotIF robot, int index) :
                base(robot, index)
            {
            }

            public override void WriteAssignment() => Offset = Robot.WriteAssignment(40, $"SR[{Index}] 1");

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

        public IRobotDataDictionary<int, RobotWritableDataHolderBase<float>> R { get; }
        public IRobotDataDictionary<int, RobotWritableDataHolderBase<Position>> PR { get; }
        public IRobotDataDictionary<int, RobotWritableDataHolderBase<string>> SR { get; }
    }
}
