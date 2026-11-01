using Fanuc.RobotInterface.Collections;

namespace Fanuc.RobotInterface
{
    public partial class ExRobotIF
    {
        public interface IRobotValueHolder : IValueHolder
        {
            void PullValue();
        }

        public interface IRobotWritableValueHolder : IRobotValueHolder, IWritableValueHolder
        {
            void PushValue();
        }

        public abstract class RobotSignalHolderBase<T> : RobotWritableDataHolderBase<T>, IRobotWritableValueHolder
        {
            public int Index { get; }

            public int Offset { get; }

            public RobotSignalHolderBase(IExRobotIF robot, int index, int offset) :
                base(robot)
            {
                Index = index;
                Offset = offset;
            }
        }

        private class DigitalInputHolder : RobotSignalHolderBase<bool>
        {
            public DigitalInputHolder(IExRobotIF robot, int index, int offset) :
                base(robot, index, offset)
            {
            }

            protected override bool ReadValue() => Robot.ReadDI(Offset + Index, 1)[0];
            protected override void WriteValue(bool value) => Robot.WriteDI(Offset + Index, value);
        }

        private class DigitalOutputHolder : RobotSignalHolderBase<bool>
        {
            public DigitalOutputHolder(IExRobotIF robot, int index, int offset) :
                base(robot, index, offset)
            {
            }

            protected override bool ReadValue() => Robot.ReadDO(Offset + Index, 1)[0];
            protected override void WriteValue(bool value) => Robot.WriteDO(Offset + Index, value);
        }

        private class GroupedInputHolder : RobotSignalHolderBase<ushort>
        {
            public GroupedInputHolder(IExRobotIF robot, int index, int offset) :
                base(robot, index, offset)
            {
            }

            protected override ushort ReadValue() => Robot.ReadGI(Offset + Index, 1)[0];
            protected override void WriteValue(ushort value) => Robot.WriteGI(Offset + Index, value);
        }

        private class GroupedOutputHolder : RobotSignalHolderBase<ushort>
        {
            public GroupedOutputHolder(IExRobotIF robot, int index, int offset) :
                base(robot, index, offset)
            {
            }

            protected override ushort ReadValue() => Robot.ReadGO(Offset + Index, 1)[0];
            protected override void WriteValue(ushort value) => Robot.WriteGO(Offset + Index, value);
        }

        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> SDI { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> SDO { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> RDI { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> RDO { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> UI { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> UO { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> SI { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> SO { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> WI { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> WO { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> WSI { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> PMC_K { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<bool>> PMC_R { get; }

        public IRobotDataDictionary<int, RobotSignalHolderBase<ushort>> GI { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<ushort>> GO { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<ushort>> AI { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<ushort>> AO { get; }
        public IRobotDataDictionary<int, RobotSignalHolderBase<ushort>> PMC_D { get; }
    }
}
