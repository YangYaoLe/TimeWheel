using System.Collections.Generic;

public abstract class BasePool
{
    public abstract int GetPoolCount();

    public abstract void Clear();
}
public class ClassObjectPool<T> : BasePool
    where T:class, new()
{
    // 池
    protected Stack<T> m_Pool = new Stack<T>();
    // 没有回收的对象个数
    protected int m_NoRecycleCount = 0;

    private int m_MaxCount = 16;

    public ClassObjectPool(int maxCount)
    {
        m_MaxCount = maxCount;
    }

    /// <summary>
    /// 取对象
    /// </summary>
    /// <param name="createIfPoolEmpty">是否强制取</param>
    /// <returns></returns>
    public T Pop(bool createIfPoolEmpty = true)
    {
        if (m_Pool.Count > 0)
        {
            T rtn = m_Pool.Pop();
            m_NoRecycleCount++;
            return rtn;
        }
        else
        {
            if (createIfPoolEmpty)
            {
                T rtn = new T();
                m_NoRecycleCount++;
                return rtn;
            }
        }
        return null;
    }

    /// <summary>
    /// 回收对象
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public bool Push(T obj)
    {
        if (obj == null)
        {
            return false;
        }

        m_NoRecycleCount--;

        if (m_Pool.Count < m_MaxCount)
        {
            m_Pool.Push(obj);
            return true;
        }

        obj = null;
        return false;
    }

    public override int GetPoolCount()
    {
        return m_Pool.Count;
    }

    public override void Clear()
    {
        m_NoRecycleCount = 0;
        if (m_Pool.Count > 0)
        {
            m_Pool.Clear();
        }
    }
}
