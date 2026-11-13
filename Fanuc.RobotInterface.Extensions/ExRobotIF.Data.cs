using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Fanuc.RobotInterface
{
    public partial class ExRobotIF
    {
        public abstract class RobotDataHolderBase<T> : INotifyPropertyChanged, IValueHolder<T>, IRobotValueHolder
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            private T _RemoteValue;
            private readonly object _ValueLock = new object();
            private long _ValueVersion;
            public T Value { get { lock (_ValueLock) return _RemoteValue; } }
            object IValueHolder.Value => Value;

            protected ExRobotIF Robot { get; }

            public RobotDataHolderBase(ExRobotIF robot)
            {
                Robot = robot ?? throw new ArgumentNullException(nameof(robot));
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void RaisePropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            protected virtual void RemoteUpdate(T value)
            {
                while (true)
                {
                    T current;
                    long version;
                    lock (_ValueLock)
                    {
                        current = _RemoteValue;
                        version = _ValueVersion;
                    }
                    var equal = EqualityComparer<T>.Default.Equals(current, value);
                    lock (_ValueLock)
                    {
                        if (version != _ValueVersion)
                            continue;
                        if (equal)
                            return;
                        _RemoteValue = value;
                        ++_ValueVersion;
                    }
                    break;
                }
                RaisePropertyChanged(nameof(Value));
            }

            void IRobotValueHolder.PullValue()
            {
                var result = Robot._InvokeIfConnected(Robot.CaptureReadySession(), () =>
                {
                    Session session;
                    lock (Robot._LifecycleLock)
                    {
                        session = Robot._Session;
                        if (session == null || !session.Ready || session.Stopping)
                            throw new System.IO.IOException("Cannot read a holder when not connected.");
                    }
                    if (this is IRobotComplexDataHolder complex)
                        complex.EnsureAssignment();
                    return Tuple.Create(session, ReadValue());
                }).GetAwaiter().GetResult();
                lock (Robot._LifecycleLock)
                {
                    if (!ReferenceEquals(Robot._Session, result.Item1) || result.Item1.Stopping)
                        return;
                }
                RemoteUpdate(result.Item2);
            }

            protected abstract T ReadValue();
        }

        public abstract class RobotWritableDataHolderBase<T> : RobotDataHolderBase<T>, IWritableValueHolder<T>, IRobotWritableValueHolder
        {
            public RobotWritableDataHolderBase(ExRobotIF robot) :
                base(robot)
            {
            }

            public async Task WriteAsync(T value)
            {
                var session = Robot.CaptureReadySession();
                if (session == null)
                    throw new System.IO.IOException("Cannot write a holder when not connected.");
                var write = PrepareWrite(value);
                await Robot._InvokeIfConnected(session, () =>
                {
                    if (this is IRobotComplexDataHolder complex)
                        complex.EnsureAssignment();
                    write();
                    return true;
                }, nameof(WriteAsync)).ConfigureAwait(false);
            }

            Task IWritableValueHolder.WriteAsync(object value) => WriteAsync((T)value);

            protected virtual Action PrepareWrite(T value) => () => WriteValue(value);
            protected abstract void WriteValue(T value);
        }

        private static Action PreparePositionWrite(ExRobotIF robot, Func<int> offset, Position value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            var displacement = value.Cartisian != null ? 0 : 26;
            var bytes = PositionConverter.GetBytes(value);
            return () => robot.WriteSNPX(offset() + displacement, bytes);
        }

        internal int AssignmentOffset { get; set; } = 1;
        private int WriteAssignmentCore(int length, string target)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (AssignmentOffset < 1 || length > 65537 - AssignmentOffset)
                throw new InvalidOperationException("The SNPX assignment range is exhausted.");
            _WriteAssignmentCommand($"SETASG {AssignmentOffset} {length} {target}");
            var n = AssignmentOffset;
            AssignmentOffset += length;
            return n;
        }

        private int WriteHolderAssignment(int length, string target) => WriteAssignmentCore(length, target);

        public Task<int> AsyncWriteAssignment(int length, string target) =>
            _InvokeIfConnected(CaptureReadySession(), () => WriteAssignmentCore(length, target));

        private interface IRobotComplexDataHolder
        {
            void EnsureAssignment();
        }

        public abstract class RobotComplexDataHolderBase<T> : RobotDataHolderBase<T>, IRobotComplexDataHolder
        {
            protected int Offset { get; set; } = -1;

            private long _AssignmentGeneration = -1;

            public RobotComplexDataHolderBase(ExRobotIF robot) :
                base(robot)
            {
            }

            protected abstract void WriteAssignment();

            void IRobotComplexDataHolder.EnsureAssignment()
            {
                if (_AssignmentGeneration == Robot.AssignmentGeneration)
                    return;
                WriteAssignment();
                _AssignmentGeneration = Robot.AssignmentGeneration;
            }
        }

        public abstract class RobotWritableComplexDataHolderBase<T> : RobotWritableDataHolderBase<T>, IRobotComplexDataHolder
        {
            protected int Offset { get; set; } = -1;

            private long _AssignmentGeneration = -1;

            protected RobotWritableComplexDataHolderBase(ExRobotIF robot) :
                base(robot)
            {
            }

            protected abstract void WriteAssignment();

            void IRobotComplexDataHolder.EnsureAssignment()
            {
                if (_AssignmentGeneration == Robot.AssignmentGeneration)
                    return;
                WriteAssignment();
                _AssignmentGeneration = Robot.AssignmentGeneration;
            }
        }

        private IEnumerable<IRobotWritableValueHolder> AllWritableSignals
        {
            get
            {
                foreach (var v in SDI) yield return v.Value;
                foreach (var v in SDO) yield return v.Value;
                foreach (var v in RDI) yield return v.Value;
                foreach (var v in RDO) yield return v.Value;
                foreach (var v in UI) yield return v.Value;
                foreach (var v in UO) yield return v.Value;
                foreach (var v in SI) yield return v.Value;
                foreach (var v in SO) yield return v.Value;
                foreach (var v in WI) yield return v.Value;
                foreach (var v in WO) yield return v.Value;
                foreach (var v in WSI) yield return v.Value;
                foreach (var v in PMC_K) yield return v.Value;
                foreach (var v in PMC_R) yield return v.Value;

                foreach (var v in GI) yield return v.Value;
                foreach (var v in GO) yield return v.Value;
                foreach (var v in AI) yield return v.Value;
                foreach (var v in AO) yield return v.Value;
                foreach (var v in PMC_D) yield return v.Value;

                foreach (var v in R) yield return v.Value;
                foreach (var v in PR) yield return v.Value;
                foreach (var v in SR) yield return v.Value;

                foreach (var v in SysVarI) yield return v.Value;
                foreach (var v in SysVarS) yield return v.Value;
                foreach (var v in SysVarP) yield return v.Value;
            }
        }

        private IEnumerable<IRobotValueHolder> AllReadableSignals
        {
            get
            {
                foreach (var v in AllWritableSignals) yield return v;
                foreach (var v in POS) yield return v.Value;
                foreach (var v in PRG) yield return v.Value;
                foreach (var v in ALM) yield return v.Value;
            }
        }
    }
}
