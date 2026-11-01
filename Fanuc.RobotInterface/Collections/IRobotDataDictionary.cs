using System.Collections.Generic;

namespace Fanuc.RobotInterface.Collections;

public interface IRobotDataDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
{
    TValue GetOrAdd(TKey key);
    bool Remove(TKey key);
    void Clear();
}