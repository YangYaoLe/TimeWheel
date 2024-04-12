using System;
using System.Collections.Generic;

public class ObjectPoolManager
{
    private static Dictionary<Type, BasePool> m_ClassPoolDict = new Dictionary<Type, BasePool>();

    private const int m_MaxPoolElementCount = 500;

    public static ClassObjectPool<T> GetOrCreatePool<T>(int maxCount = 16) where T : class, new()
    {
        maxCount = ((maxCount > m_MaxPoolElementCount) ? m_MaxPoolElementCount : maxCount);
        Type type = typeof(T);
        if (!m_ClassPoolDict.TryGetValue(type, out BasePool outObj))
        {
            ClassObjectPool<T> newPool = new ClassObjectPool<T>(maxCount);
            m_ClassPoolDict.Add(type, newPool);
            return newPool;
        }
        else
        {
            return outObj as ClassObjectPool<T>;
        }
    }

    public static void ClearAllPool()
    {
        foreach (var pool in m_ClassPoolDict.Values)
        {
            pool.Clear();
        }
        m_ClassPoolDict?.Clear();
    }
}