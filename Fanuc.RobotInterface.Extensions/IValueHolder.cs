using System.Threading.Tasks;

namespace Fanuc.RobotInterface;

public interface IValueHolder
{
    object Value { get; }
}

public interface IWritableValueHolder : IValueHolder
{
    Task WriteAsync(object value);
}

public interface IValueHolder<T> : IValueHolder
{
    new T Value { get; }
}

public interface IWritableValueHolder<T> : IValueHolder<T>, IWritableValueHolder
{
    Task WriteAsync(T value);
}