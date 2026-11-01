using Fanuc.RobotInterface.Collections;
using System;

namespace Fanuc.RobotInterface
{
    public partial class ExRobotIF
    {
        public class RobotCurrentPositionHolder : RobotComplexDataHolderBase<Position>
        {
            public uint Group { get; }
            public uint UserFrame { get; }

            public RobotCurrentPositionHolder(IExRobotIF robot, uint frame = 0, uint group = 1) :
                base(robot)
            {
                Group = group < 1 ? 1 : group;
                UserFrame = frame;
                if (Robot.IsConnected)
                    WriteAssignment();
            }

            public override void WriteAssignment() => Offset = Robot.WriteAssignment(50, $"POS[G{Group}:{UserFrame}] 0.0");

            protected override Position ReadValue() => Offset == -1 ? null : PositionConverter.ToPosition(Robot.ReadSNPX(Offset, 100));
        }

        public class RobotTaskHolder : RobotComplexDataHolderBase<RobotTaskStatus>
        {
            public RobotTaskType TaskType { get; }
            public int Index { get; }

            public RobotTaskHolder(IExRobotIF robot, int index, RobotTaskType type = RobotTaskType.All) :
                base(robot)
            {
                TaskType = type;
                Index = index;
                if (Robot.IsConnected)
                    WriteAssignment();
            }

            public override void WriteAssignment() =>
                Offset = Robot.WriteAssignment(18, _TaskTypeToAssignmentString(TaskType));

            protected override RobotTaskStatus ReadValue() => Offset == -1 ? null : RobotTaskStatus.FromBytes(Robot.ReadSNPX(Offset, 36));

            private string _TaskTypeToAssignmentString(RobotTaskType t)
            {
                switch (t)
                {
                    case RobotTaskType.All: return $"PRG[{Index}] 1";
                    case RobotTaskType.IgnoreMacro: return $"PRG[M{Index}] 1";
                    case RobotTaskType.IgnoreKarel: return $"PRG[K{Index}] 1";
                    case RobotTaskType.IgnoreMacroKarel: return $"PRG[MK{Index}] 1";
                    default:
                        throw new ArgumentException($"Unknown RobotTaskType {TaskType}", nameof(TaskType));
                }
            }
        }

        public class RobotAlarmHolder : RobotComplexDataHolderBase<RobotAlarm>
        {
            public bool IsHistory { get; }
            public int Index { get; }

            public RobotAlarmHolder(IExRobotIF robot, int index, bool history = false) :
                base(robot)
            {
                Index = index;
                IsHistory = history;
                if (Robot.IsConnected)
                    WriteAssignment();
            }

            public override void WriteAssignment() => Offset = Robot.WriteAssignment(100, $"ALM[{(IsHistory ? "E" : "")}{Index}] 1");

            protected override RobotAlarm ReadValue() => Offset == -1 ? null : RobotAlarm.FromBytes(Robot.ReadSNPX(Offset, 200));
        }

        public IRobotDataDictionary<uint, RobotDataHolderBase<Position>> POS { get; }
        public IRobotDataDictionary<int, RobotDataHolderBase<RobotTaskStatus>> PRG { get; }
        public IRobotDataDictionary<int, RobotDataHolderBase<RobotAlarm>> ALM { get; }
    }
}
