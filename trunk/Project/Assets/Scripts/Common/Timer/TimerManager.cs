using System;
using System.Collections.Generic;
using UnityEngine;

public class TimerManager
{
    private static TimerManager m_Instance;
    public static TimerManager Instance
    {  
        get 
        { 
            if (m_Instance == null)
            {
                m_Instance = new TimerManager();
            }
            return m_Instance;
        }
    }

    #region 常量

    // tv0 8位编码值：0-255,256个时间槽
    private const int k_Tv0Bits = 8;
    // tv1-tv4 6位编码值：0-63,64个时间槽
    private const int k_TvxBits = 6;
    // tv0 时间槽长度
    private const int k_Tv0Size = 1 << k_Tv0Bits;
    // tv1-tv4 时间槽长度
    private const int k_TvxSize = 1 << k_TvxBits;

    private const int k_Tv0Mask = k_Tv0Size - 1;
    private const int k_TvxMask = k_TvxSize - 1;

    // tv0-tv4 5个时间轮在m_TimerSlotes里的起始偏移量
    private const int k_Tv0Offset = 0;
    private const int k_Tv1Offset = k_Tv0Size;
    private const int k_Tv2Offset = k_Tv0Size + 1 * k_TvxSize;
    private const int k_Tv3Offset = k_Tv0Size + 2 * k_TvxSize;
    private const int k_Tv4Offset = k_Tv0Size + 3 * k_TvxSize;

    // tv0-tv4 表示能Tick的最大值+1
    private const long k_Tv0TickMax = 1 << k_Tv0Bits;
    private const long k_Tv1TickMax = 1 << (k_Tv0Bits + k_TvxBits);
    private const long k_Tv2TickMax = 1 << (k_Tv0Bits + k_TvxBits * 2);
    private const long k_Tv3TickMax = 1 << (k_Tv0Bits + k_TvxBits * 3);
    private const long k_Tv4TickMax = 1 << (k_Tv0Bits + k_TvxBits * 4);

    // 每个Tick经过多少毫秒
    private const int k_MsPerTick = 10;
    private const float k_SecondPerTick = k_MsPerTick / 1000f;

    #endregion

    #region 数据定义

    // 一个数组拆分成5个逻辑数组，代表tv0-tv4的槽
    private TimerLinkedList[] m_TimerSlots = new TimerLinkedList[256 + 64 * 4];
    private TimerLinkedList m_SwapList = new TimerLinkedList();

    // 初始tick
    private long m_TickStart = 0;
    // 已检测的Tick
    private long m_TickChecked;
    // 当前需要执行到的Tick
    private long m_CurTick;
    // id种子
    private int m_IdSeed = 0;
    // 所有注册了的Timer
    private Dictionary<int, Timer> m_AllTimer = new Dictionary<int, Timer>();
    // Timer对象池
    private ClassObjectPool<Timer> m_TimerPool;
    // 延迟回收池
    private List<Timer> m_DelayRecycleList = new(); // update的时候不能回池，会导致数组错乱

    #endregion

    #region 生命周期

    public void Init()
    {
        for (int i = 0; i < m_TimerSlots.Length; i++)
        {
            m_TimerSlots[i] = new TimerLinkedList();
        }

        m_TickStart = CurTick();
        m_TickChecked = CurTick();
        m_CurTick = CurTick();
        m_TimerPool = ObjectPoolManager.GetOrCreatePool<Timer>(16);
    }

    public void Update()
    {
        long nowTick = CurTick();
        m_CurTick = nowTick;
        while (m_TickChecked < nowTick)
        {
            int index = (int)(m_TickChecked & k_Tv0Mask);
            //满一圈则推动上级轮tick
            if (index == 0
                && Cascade(k_Tv1Offset, CalculIndex(0, m_TickChecked)) == 0
                && Cascade(k_Tv2Offset, CalculIndex(1, m_TickChecked)) == 0
                && Cascade(k_Tv3Offset, CalculIndex(2, m_TickChecked)) == 0)
            {
                Cascade(k_Tv4Offset, CalculIndex(3, m_TickChecked));
            }

            //当前的前256个的某个列表一定是到期的，执行该列表的timer
            TimerLinkedList nowList = m_TimerSlots[index];
            if (nowList.Count > 0)
            {
                m_TimerSlots[index] = m_SwapList;
                m_SwapList = nowList;
                Timer temp = m_SwapList.GetFirst<Timer>();
                while (temp != null)
                {
                    Timer timer = temp;
                    temp = temp.Next as Timer;
                    timer.Execute();
                    if (timer.IsDoneOrInValid())
                    {
                        RemoveTimer(timer.Id);
                        timer.Reset();
                    }
                    else
                    {
                        timer.ToNextExpires(nowTick);
                        Add(timer);
                    }
                }
                // 清空当前列表
                m_SwapList.Clear();
            }
            ++m_TickChecked;
        }

        if (m_DelayRecycleList.Count > 0)
        {
            foreach (var item in m_DelayRecycleList)
            {
                m_TimerPool.Push(item);
            }
            m_DelayRecycleList.Clear();
        }
    }

    public void Destroy()
    {
        RemoveAll();
    }

    #endregion

    #region  内部接口

    // 毫秒转Tick
    private long MsecToTick(long millisecond)
    {
        // 当前最小精度就是毫秒，直接返回
        return millisecond / k_MsPerTick;
    }

    private long CurTick()
    {
        // 定时器的精度为1毫秒，这里使用系统滴答数，系统一个tick是100纳秒
        return (long)(Time.time/k_SecondPerTick)-m_TickStart;
    }

    // 算出在对应x的时间轮里的槽位
    private int CalculIndex(byte x, long expires)
    {
        return (int)(expires >> (k_Tv0Bits + x * k_TvxBits) & k_TvxMask);
    }

    private void Add(Timer timer)
    {
        long expires = timer.Expires;
        long nowTick = m_CurTick;
        long tickSpan = expires - nowTick;

        if (tickSpan < 0)
        {
            // 时间过了，下一帧执行
            timer.Index = (int)(nowTick & k_Tv0Mask);
        }
        else if(tickSpan < k_Tv0TickMax)
        {
            // <256，放到tv0
            timer.Index = (int)(expires & k_Tv0Mask);
        }
        else if (tickSpan < k_Tv1TickMax)
        {
            timer.Index = k_Tv1Offset + CalculIndex(0, expires);
        }
        else if (tickSpan < k_Tv2TickMax)
        {
            timer.Index = k_Tv2Offset + CalculIndex(1, expires);
        }
        else if (tickSpan < k_Tv3TickMax)
        {
            timer.Index = k_Tv3Offset + CalculIndex(2, expires);
        }
        else
        {
            if (tickSpan > k_Tv4TickMax)
            {
                expires = k_Tv4TickMax + nowTick; // 限定一下最大值
            }
            timer.Index = k_Tv4Offset + CalculIndex(3, expires);
        }
        
        TimerLinkedList targetList = m_TimerSlots[timer.Index];
        targetList.AddLast(timer);
    }

    private void Remove(Timer timer)
    {
        if (timer.Index >= 0)
        {
            TimerLinkedList targetList = m_TimerSlots[timer.Index];
            targetList.Remove(timer);
        }
    }

    // 高阶时间轮任务下派
    private int Cascade(int offset, int slot)
    {
        int index = offset + slot;
        TimerLinkedList nowList = m_TimerSlots[index];
        if (nowList.Count > 0)
        {
            m_TimerSlots[index] = m_SwapList;
            m_SwapList = nowList;
            Timer temp = m_SwapList.GetFirst<Timer>();
            while (temp != null)
            {
                Timer timer = temp;
                temp = temp.Next as Timer;
                Add(timer);
            }
            m_SwapList.Clear();
        }
        return slot;
    }
    #endregion

    #region 外部需要封装的接口

    public int AddTimer(Action<object[]> callback, float interval, bool delay = true, int times = 1, object[] callParam = null)
    {
        if (interval <= 0)
        {
            UnityEngine.Debug.LogError("Add timer failed! The interval cannot be less than zero");
            return -1;
        }
        if (times == 0)
        {
            return -1;
        }
        Timer timer = m_TimerPool.Pop();
        long nowTick = CurTick();
        long tickInterval = MsecToTick((long)(interval * 1000));
        timer.Init(++m_IdSeed, nowTick, delay, tickInterval, times, callback, callParam);
        m_AllTimer.Add(timer.Id, timer);

        Add(timer);
        return timer.Id;
    }

    public bool RemoveTimer(int timerId)
    {
        if (m_AllTimer.ContainsKey(timerId))
        {
            Timer timer = m_AllTimer[timerId];
            timer.IsValid = false;
            Remove(timer);
            m_AllTimer.Remove(timerId);
            
            if (m_TickChecked < m_CurTick)
            {
                // update的时候回池会导致数组错乱，延迟回收
                m_DelayRecycleList.Add(timer);
            }
            else
            {
                m_TimerPool.Push(timer);
            }
            return true;
        }
        return false;
    }

    public bool ModifyTimer(int timerId, float interval, int times = 1)
    {
        if (interval <= 0)
        {
            return false;
        }
        if (times == 0)
        {
            return false;
        }
        if (m_AllTimer.ContainsKey(timerId))
        {
            Timer timer = m_AllTimer[timerId];
            Remove(timer);

            long nowTick = CurTick();
            long tickInterval = MsecToTick((long)(interval * 1000));
            timer.Modify(nowTick, tickInterval, times);

            Add(timer);
            return true;
        }
        return false;
    }

    public void RemoveAll()
    {
        foreach (TimerLinkedList timerList in m_TimerSlots)
        {
            timerList.Clear();
        }

        if (m_TimerPool != null)
        {
            m_TimerPool.Clear();
        }

        m_AllTimer.Clear();
        m_DelayRecycleList.Clear();
    }
    #endregion

    #region 定时器任务

    private class Timer : TimerLinkedNode
    {
        // 定时器ID
        public int Id { get; set; }
        // 所属TimerLinkedList所在的数组下标
        public int Index { get; set; }
        // 到期Tick数
        public long Expires { get; set; }
        // 是否启用定时器任务
        public bool IsValid { get; set; }
        // 回调
        private Action<object[]> m_Callback;
        // 附加参数
        private object[] m_Param;
        // 循环间隔Tick数
        private long m_Interval;
        // 循环次数， -1无限循环，>0指定次数
        private int m_Times;

        public Timer()
        {
            Reset();
        }

        public void Init(int id, long nowTick, bool delay, long interval, int times, Action<object[]> callback, object[] callbackParam = null)
        {
            Id = id;
            m_Interval = interval;
            m_Times = times == 0 ? -1 : times;
            m_Param = callbackParam;
            m_Callback = callback;
            Expires = delay ? (nowTick + interval) : nowTick;
            IsValid = true;
        }

        public void Modify(long nowTick, long interval, int times)
        {
            m_Interval = interval;
            m_Times = times == 0 ? -1 : times;
            Expires = nowTick + interval;
            IsValid = true;
        }

        public bool IsDoneOrInValid()
        {
            if (!IsValid)
            {
                return true;
            }

            if (m_Times < 0)
            {
                return false;
            }
            else
            {
                m_Times--;
                return m_Times == 0;
            }
        }

        public void ToNextExpires(long nowTick)
        {
            Expires = nowTick + m_Interval;
        }

        public void Execute()
        {
            try
            {
                m_Callback(m_Param);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("Run Timer Exception: " + e.ToString());
            }
        }

        public void Reset()
        {
            Index = -1;
            Expires = 0;
            IsValid = false;
            m_Callback = null;
            m_Param = null;
            m_Interval = 0;
            m_Times = 0;
        }

    }

    #endregion

    #region 定时器专用LinkedList

    private class TimerLinkedNode
    {
        public TimerLinkedList List;
        public TimerLinkedNode Next;
        public TimerLinkedNode Prev;

        public void ClearLink()
        {
            List = null;
            Prev = Next = null;
        }
    }

    private class TimerLinkedList
    {
        private TimerLinkedNode m_First;
        private TimerLinkedNode m_Last;
        private int m_Count;
        public int Count
        {
            get
            {
                return m_Count;
            }
        }

        public T GetFirst<T>() where T : TimerLinkedNode
        {
            return m_First as T;
        }

        public TimerLinkedNode AddLast(TimerLinkedNode value)
        {
            if (value != null)
            {
                if (value.List != null)
                {
                    Remove(value);
                }
                if (m_First == null)
                {
                    value.Prev = value.Next = null;
                    m_First = m_Last = value;
                }
                else
                {
                    m_Last.Next = value;
                    value.Prev = m_Last;
                    value.Next = null;
                    m_Last = value;
                }
                value.List = this;
                m_Count++;
                return value;
            }
            return null;
        }

        public bool Remove(TimerLinkedNode value)
        {
            if (value != null && value.List != null)
            {
                value.List.DoRemove(value);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            m_First = m_Last = null;
            m_Count = 0;
        }

        private bool DoRemove(TimerLinkedNode value)
        {
            if (value != null && value.List == this)
            {
                if (value.Prev != null)
                {
                    value.Prev.Next = value.Next;
                }
                if (value.Next != null)
                {
                    value.Next.Prev = value.Prev;
                }
                if (m_First == value)
                {
                    m_First = m_First.Next;
                }
                if (m_Last == value)
                {
                    m_Last = m_Last.Prev;
                }

                m_Count--;
                value.List = null;
                value.Prev = value.Next = null;

                return true;
            }
            return false;
        }
    }

    #endregion
}

