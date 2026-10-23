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
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private long _hourTick;
    private long _up;
    private long _down;
    private bool _dirty;
    private DateTime _lastFlush = DateTime.MinValue;

    private CancellationTokenSource? _pollCts;
    private readonly TrafficDeltaTracker _tracker = new();

    /// <summary>
    /// 开始自行轮询 xray 的 /debug/vars。
    ///
    /// 为什么不复用统计服务算好的每秒增量：那条链路上游会因内核重启重置基线并
    /// 直接丢弃当轮结果（ParseOutput 里 ProxyDown 变小就 return null），
    /// 实测一整段 4 MB 的下载没能进入增量，页面长期只有几 KB。
    /// 这里直接读累计值自己作差，逻辑简单、不依赖上游的重试与重置策略。
    /// 间隔 5 秒：本机回环请求，开销可以忽略，且比分秒级更省 CPU。
    /// </summary>
    public void StartPolling()
    {
        Close();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    var totals = await ReadTotalsAsync(token).ConfigureAwait(false);
                    if (totals is null)
                    {
                        continue;
                    }

                    var (up, down) = totals.Value;
                    // 首次读数只作基线；内核重启导致计数归零时同样只重置基线，
                    // 不会把负差额算成流量
                    var delta = _tracker.Feed(up, down);
                    if (delta.Up > 0 || delta.Down > 0)
                    {
                        Add(delta.Up, delta.Down);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"{_tag}.Poll", ex);
                }
            }
        }, token);
    }

    public void Close()
    {
        try
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
        }
        catch
        {
            // 关闭失败不影响退出
        }
    }

    /// <summary>
    /// 读取当前内核的累计上下行字节。
    ///
    /// 两种内核走两条路：
    ///   xray  → /debug/vars 的 stats（按 outbound 分项累加）
    ///   sing-box / mihomo → clash API 的 uploadTotal / downloadTotal
    /// 之前只实现了 xray 那条，于是切到 sing-box 后「概览」永远是 0。
    /// </summary>
    private static async Task<(long Up, long Down)?> ReadTotalsAsync(CancellationToken token)
    {
        var running = AppManager.Instance.RunningCoreType;

        if (running is ECoreType.sing_box or ECoreType.mihomo)
        {
            var clashUrl = $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort2}/connections";
            var clashRaw = await HttpClientHelper.Instance.TryGetAsync(clashUrl, token).ConfigureAwait(false);
            return TrafficTotalsParser.ParseClashTotals(clashRaw);
        }

        if (running is not (ECoreType.Xray or ECoreType.v2fly or ECoreType.v2fly_v5))
        {
            return null;
        }

        var url = $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort}/debug/vars";
        var raw = await HttpClientHelper.Instance.TryGetAsync(url, token).ConfigureAwait(false);
        return raw.IsNullOrEmpty() ? null : ReadProxyTotals(raw);
    }

    /// <summary>
    /// 从 /debug/vars 里取 proxy 出站的累计 uplink/downlink（字节）。
    /// 返回 -1 表示没找到，调用方据此跳过本轮。
    /// </summary>
    private static (long up, long down) ReadProxyTotals(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("stats", out var stats)
                || !stats.TryGetProperty("outbound", out var outbound)
                || outbound.ValueKind != JsonValueKind.Object)
            {
                return (-1, -1);
            }

            long up = 0;
            long down = 0;
            var found = false;
            foreach (var prop in outbound.EnumerateObject())
            {
                if (!prop.Name.StartsWith(Global.ProxyTag, StringComparison.Ordinal))
                {
                    continue;
                }

                found = true;
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    if (prop.Value.TryGetProperty("uplink", out var u) && u.TryGetInt64(out var uv))
                    {
                        up += uv;
                    }

                    if (prop.Value.TryGetProperty("downlink", out var d) && d.TryGetInt64(out var dv))
                    {
                        down += dv;
                    }
                }
            }

            return found ? (up, down) : (-1, -1);
        }
        catch
        {
            return (-1, -1);
        }
    }

    /// <summary>
    /// 累加一次增量。由 StatisticsManager 每秒调用，必须够轻 —— 只做内存加法。
    /// </summary>
    public void Add(long up, long down)
    {
        if (up <= 0 && down <= 0)
        {
            return;
        }
        var needFlush = false;
        lock (_lock)
        {
            var hour = CurrentHourTick();
            if (_hourTick != hour)
            {
                // 跨小时：先把上一小时的余量刷掉，再从新桶开始。
                // 不刷的话这一小时最后不足一个间隔的流量会被算进下一小时。
                needFlush = true;
                _hourTick = hour;
            }

            _up += Math.Max(0, up);
            _down += Math.Max(0, down);
            _dirty = true;

            if (DateTime.Now - _lastFlush >= FlushInterval)
            {
                needFlush = true;
            }
        }

        // 锁外再排落库：写库是 IO，不能占着锁做，否则 UI 线程取数据时会被堵住
        if (needFlush)
        {
            _ = FlushAsync();
        }
    }

    /// <summary>
    /// 排一次落库（不阻塞调用方）。
    ///
    /// 这里刻意不再「持锁等异步写库」：这个服务的调用点包含 UI 线程
    /// （切到流量页会取当日/近几日数据），一旦在锁内同步等待 SQLite，
    /// 只要写库那一刻被占用，界面就会整块卡死 —— 实测点「流量」标签直接卡住。
    /// 现在只为写库拍快照，真正落库在锁外异步完成。
    /// </summary>
    public void Flush()
    {
        _ = FlushAsync();
    }

    public async Task FlushAsync()
    {
        long hour;
        long up;
        long down;
        lock (_lock)
        {
            _lastFlush = DateTime.Now;
            if (!_dirty || _hourTick == 0)
            {
                return;
            }

            // 快照后立刻清脏标记：写库在锁外进行，期间新的增量会重新置脏，
            // 下一轮 flush 再写一次，不会丢。
            hour = _hourTick;
            up = _up;
            down = _down;
            _dirty = false;
        }

        try
        {
            var table = SQLiteHelper.Instance.TableAsync<TrafficStatItem>();
            var row = await table.Where(t => t.HourTick == hour).FirstOrDefaultAsync();
            if (row is null)
            {
                await SQLiteHelper.Instance.ReplaceAsync(new TrafficStatItem
                {
                    HourTick = hour,
                    Up = up,
                    Down = down,
                });
            }
            else
            {
                row.Up = up;
                row.Down = down;
                await SQLiteHelper.Instance.ReplaceAsync(row);
            }
        }
        catch (Exception ex)
        {
            // 落库失败重新置脏，下一轮重试；数据仍在内存里
            lock (_lock)
            {
                _dirty = true;
            }

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
        await FlushAsync();

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
        await FlushAsync();

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
