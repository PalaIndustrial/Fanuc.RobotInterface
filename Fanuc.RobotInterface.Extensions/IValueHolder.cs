namespace Fanuc.RobotInterface;

public interface IValueHolder
{
    object Value { get; }
}

public interface IWritableValueHolder : IValueHolder
{
    new object Value { get; set; }
}

public interface IValueHolder<T> : IValueHolder
{
    new T Value { get; }
}

public interface IWritableValueHolder<T> : IValueHolder<T>, IWritableValueHolder
{
    new T Value { get; set; }
}