namespace ServiceLib.Services;

/// <summary>
/// 按小时累计代理流量并落库。
///
/// 为什么不直接每秒写库：统计服务是 1 秒一轮，每轮都写 SQLite 会让磁盘
/// 持续繁忙（笔记本上很费电）。这里先在内存里累加，按固定间隔批量落库；
/// 退出时再强制刷一次，最多丢一个刷盘间隔的数据。
/// </summary>
public class TrafficStatService
{
    private static readonly string _tag = "TrafficStatService";
    private static readonly Lazy<TrafficStatService> _instance = new(() => new());
    public static TrafficStatService Instance => _instance.Value;

    /// <summary>落库间隔。20 秒：既不会让磁盘一直响，异常退出最多也只丢这么久。</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(20);

    private readonly object _lock = new();
    private long _hourTick;
    private long _up;
    private long _down;
    private bool _dirty;
    private DateTime _lastFlush = DateTime.MinValue;

    /// <summary>
    /// 累加一次增量。由 StatisticsManager 每秒调用，必须够轻 —— 只做内存加法。
    /// </summary>
    public void Add(long up, long down)
    {
        if (up <= 0 && down <= 0)
        {
            return;
        }

        lock (_lock)
        {
            var hour = CurrentHourTick();
            if (_hourTick != hour)
            {
                // 跨小时：先把上一小时的余量刷掉，再从新桶开始。
                // 不刷的话这一小时最后不足一个间隔的流量会被算进下一小时。
                FlushLocked();
                _hourTick = hour;
            }

            _up += Math.Max(0, up);
            _down += Math.Max(0, down);
            _dirty = true;

            if (DateTime.Now - _lastFlush >= FlushInterval)
            {
                FlushLocked();
            }
        }
    }

    /// <summary>把内存里的累计值写入数据库。可安全重复调用。</summary>
    public void Flush()
    {
        lock (_lock)
        {
            FlushLocked();
        }
    }

    public async Task FlushAsync()
    {
        await Task.Run(Flush);
    }

    private void FlushLocked()
    {
        _lastFlush = DateTime.Now;
        if (!_dirty || _hourTick == 0)
        {
            return;
        }

        var hour = _hourTick;
        var up = _up;
        var down = _down;

        // 这里是同步落库：调用点在每秒的统计回调里，改成异步没人 await，
        // 异常会被吞掉。SQLite 单行 upsert 足够快（实测微秒级），
        // 且已有 20 秒节流，不会成为瓶颈。
        try
        {
            var table = SQLiteHelper.Instance.TableAsync<TrafficStatItem>();
            // SQLite-net 的异步表查询只提供 *Async 版本，同步的 FirstOrDefault
            // 会撞上 ImmutableArray 的扩展方法推断失败；这里是同步上下文，直接等结果。
            var row = table.Where(t => t.HourTick == hour).FirstOrDefaultAsync().GetAwaiter().GetResult();
            if (row is null)
            {
                SQLiteHelper.Instance.ReplaceAsync(new TrafficStatItem
                {
                    HourTick = hour,
                    Up = up,
                    Down = down,
                }).GetAwaiter().GetResult();
            }
            else
            {
                row.Up = up;
                row.Down = down;
                SQLiteHelper.Instance.ReplaceAsync(row).GetAwaiter().GetResult();
            }

            _dirty = false;
        }
        catch (Exception ex)
        {
            // 落库失败不清 _dirty：下一轮再试，数据仍在内存里。
            Logging.SaveLog($"{_tag}.Flush", ex);
        }
    }

    /// <summary>
    /// 把数据库里已有的、当前小时的累计值读回内存。
    /// 不做这一步，重启后会从 0 开始覆盖掉这一小时已经记下的流量。
    /// </summary>
    public async Task LoadCurrentHourAsync()
    {
        try
        {
            var hour = CurrentHourTick();
            var table = SQLiteHelper.Instance.TableAsync<TrafficStatItem>();
            var row = await table.Where(t => t.HourTick == hour).FirstOrDefaultAsync();
            lock (_lock)
            {
                _hourTick = hour;
                _up = row?.Up ?? 0;
                _down = row?.Down ?? 0;
                _dirty = false;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.LoadCurrentHour", ex);
        }
    }

    /// <summary>最近 N 小时的小时桶，缺的小时补 0，便于界面直接画连续柱状图。</summary>
    public async Task<List<TrafficStatItem>> GetHourlyAsync(int hours)
    {
        Flush();

        var result = new List<TrafficStatItem>(hours);
        try
        {
            var now = DateTime.Now;
            var end = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            var start = end.AddHours(-(hours - 1));

            var table = SQLiteHelper.Instance.TableAsync<TrafficStatItem>();
            var rows = await table.Where(t => t.HourTick >= start.Ticks && t.HourTick <= end.Ticks).ToListAsync();
            var map = rows.ToDictionary(t => t.HourTick);

            for (var i = 0; i < hours; i++)
            {
                var tick = start.AddHours(i).Ticks;
                result.Add(map.TryGetValue(tick, out var row)
                    ? row
                    : new TrafficStatItem { HourTick = tick });
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.GetHourly", ex);
        }

        return result;
    }

    /// <summary>最近 N 天按天汇总（由小时桶加出来）。</summary>
    public async Task<List<TrafficStatItem>> GetDailyAsync(int days)
    {
        Flush();

        var result = new List<TrafficStatItem>(days);
        try
        {
            var today = DateTime.Now.Date;
            var start = today.AddDays(-(days - 1));
            var endTick = today.AddDays(1).Ticks;

            var table = SQLiteHelper.Instance.TableAsync<TrafficStatItem>();
            var rows = await table.Where(t => t.HourTick >= start.Ticks && t.HourTick < endTick).ToListAsync();

            var buckets = new Dictionary<long, TrafficStatItem>();
            foreach (var row in rows)
            {
                // 用当天的零点作为日桶的键，语义与小时桶一致
                var dayTick = new DateTime(row.HourTick).Date.Ticks;
                if (!buckets.TryGetValue(dayTick, out var bucket))
                {
                    bucket = new TrafficStatItem { HourTick = dayTick };
                    buckets[dayTick] = bucket;
                }

                bucket.Up += row.Up;
                bucket.Down += row.Down;
            }

            for (var i = 0; i < days; i++)
            {
                var tick = start.AddDays(i).Ticks;
                result.Add(buckets.TryGetValue(tick, out var bucket)
                    ? bucket
                    : new TrafficStatItem { HourTick = tick });
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.GetDaily", ex);
        }

        return result;
    }

    /// <summary>清空全部流量历史。</summary>
    public async Task ClearAsync()
    {
        try
        {
            await SQLiteHelper.Instance.ExecuteAsync("delete from TrafficStatItem");
            lock (_lock)
            {
                _up = 0;
                _down = 0;
                _dirty = false;
                _hourTick = CurrentHourTick();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.Clear", ex);
        }
    }

    private static long CurrentHourTick()
    {
        var now = DateTime.Now;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).Ticks;
    }
}
