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


            public RobotRegisterHolderBase(ExRobotIF robot, int index) :
                base(robot)
            {
                Index = index;
            }
        }

        private class RobotNumericRegisterHolder : RobotRegisterHolderBase<float>
        {
            public RobotNumericRegisterHolder(ExRobotIF robot, int index) :
                base(robot, index)
            {
            }

            protected override void WriteAssignment() => Offset = Robot.WriteHolderAssignment(2, $"R[{Index}] 0");

            protected override void WriteValue(float value)
            {
                Robot.WriteSNPX(Offset, BitConverter.GetBytes(value));
            }

            protected override float ReadValue() => Offset == -1 ? default : BitConverter.ToSingle(Robot.ReadSNPX(Offset, 4), 0);
        }

        private class RobotPositionRegisterHolder : RobotRegisterHolderBase<Position>
        {
            public RobotPositionRegisterHolder(ExRobotIF robot, int index) :
                base(robot, index)
            {
            }

            protected override void WriteAssignment() => Offset = Robot.WriteHolderAssignment(50, $"PR[{Index}] 0.0");

            protected override Action PrepareWrite(Position value) => PreparePositionWrite(Robot, () => Offset, value);
            protected override void WriteValue(Position value) => PrepareWrite(value)();

            protected override Position ReadValue() => Offset == -1 ? null : PositionConverter.ToPosition(Robot.ReadSNPX(Offset, 100));
        }

        private class RobotStringRegisterHolder : RobotRegisterHolderBase<string>
        {
            public RobotStringRegisterHolder(ExRobotIF robot, int index) :
                base(robot, index)
            {
            }

            protected override void WriteAssignment() => Offset = Robot.WriteHolderAssignment(40, $"SR[{Index}] 1");

            protected override string ReadValue() => Offset == -1 ? null : Encoding.ASCII.GetString(Robot.ReadSNPX(Offset, 80));

            protected override void WriteValue(string value)
            {
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
