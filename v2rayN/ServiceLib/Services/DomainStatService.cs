namespace ServiceLib.Services;

/// <summary>
/// 按域名累计代理流量。
///
/// 数据来源与「时段统计」不同，这里只能走 clash API：
///   GET {clashApi}/connections 返回每条连接的 host / upload / download。
/// xray 内核没有等价接口（它的 stats 只有 inbound/outbound/user 三个维度），
/// 所以本服务仅在 mihomo / sing-box 内核下有效 —— 这一点必须由界面如实告知，
/// 不能让用户以为是功能坏了。
///
/// 累计方式：clash 给的是**每条连接的累计值**，连接断开后从列表里消失。
/// 因此按连接 id 记住上一次的读数、只累加增量；连接消失时它的最后增量
/// 已经在上一轮记过了，不需要补。
/// </summary>
public class DomainStatService
{
    private static readonly string _tag = "DomainStatService";
    private static readonly Lazy<DomainStatService> _instance = new(() => new());
    public static DomainStatService Instance => _instance.Value;

    // clash 的 /connections 只列"当前活跃"的连接：连接一关就消失，
// 所以轮询周期直接决定漏计多少。实测 2MB 的下载 1 秒结束，5 秒周期完全抓不到。
    // 1 秒换来的开销只是一次本地 HTTP GET，可以接受；仍然会漏掉亚秒级短连接，
    // 这点在界面上如实说明，不假装精确。
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();

    /// <summary>连接 id → 上一轮读到的 (up, down)。</summary>
    private readonly Dictionary<string, (ulong Up, ulong Down)> _seen = [];
    private readonly Dictionary<string, (long Up, long Down)> _pending = [];
    private DateTime _lastFlush = DateTime.MinValue;

    private CancellationTokenSource? _cts;

    /// <summary>本服务是否能在当前内核下工作。</summary>
    public static bool IsSupported => AppManager.Instance.RunningCoreType is ECoreType.mihomo or ECoreType.sing_box;

    /// <summary>
    /// 当前内核是否支持 clash API。用于界面提示，不触发任何 IO。
    /// </summary>
    public static string UnsupportedHint =>
        AppManager.Instance.RunningCoreType switch
        {
            ECoreType.mihomo or ECoreType.sing_box => string.Empty,
            ECoreType.Xray or ECoreType.v2fly or ECoreType.v2fly_v5 =>
                "按域名统计依赖 clash API，当前 Xray 内核不提供。到「设置 → 高级功能 → 核心设置 → Core 类型设置」，把 VLESS 那一行的内核改成 sing_box（下拉里就叫 sing_box），保存后重连节点即可。",
            _ => "内核未运行。改用 sing_box 内核连接节点后即可按域名统计（内核在「设置 → 高级功能 → 核心设置 → Core 类型设置」里指定）。",
        };

    public void StartPolling()
    {
        Close();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    if (!IsSupported)
                    {
                        // 内核切换时清掉连接快照：新内核的连接 id 完全换了一批，
                        // 留着旧快照只会让第一轮把"新增连接"重复计入。
                        ResetSeen();
                        continue;
                    }

                    var conns = await ClashApiManager.Instance.GetConnections().ConfigureAwait(false);
                    if (conns?.connections is null)
                    {
                        continue;
                    }

                    Accumulate(conns.connections);
                    MaybeFlush();
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
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch
        {
            // 退出路径不该再抛
        }
    }

    private void Accumulate(List<ConnectionItem> list)
    {
        var today = DateTime.Now.ToString("yyyyMMdd");
        var alive = new HashSet<string>();

        lock (_lock)
        {
            foreach (var conn in list)
            {
                if (conn.id.IsNullOrEmpty())
                {
                    continue;
                }

                var id = conn.id!;
                alive.Add(id);

                (ulong Up, ulong Down) prev = _seen.TryGetValue(id, out var v) ? v : (0UL, 0UL);
                // 计数回退说明连接被复用/重置，按新增处理，不累负值
                var dUp = conn.upload >= prev.Up ? conn.upload - prev.Up : conn.upload;
                var dDown = conn.download >= prev.Down ? conn.download - prev.Down : conn.download;
                _seen[id] = (conn.upload, conn.download);

                if (dUp == 0 && dDown == 0)
                {
                    continue;
                }

                var host = ResolveHost(conn.metadata);
                if (host.IsNullOrEmpty())
                {
                    continue;
                }

                var key = $"{today}|{host}";
                var cur = _pending.TryGetValue(key, out var p) ? p : (0L, 0L);
                _pending[key] = (cur.Item1 + (long)dUp, cur.Item2 + (long)dDown);
            }

            // 已断开的连接不再跟踪，避免字典无限增长
            foreach (var id in _seen.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                _seen.Remove(id);
            }
        }
    }

    private static string? ResolveHost(MetadataItem? meta)
    {
        if (meta is null)
        {
            return null;
        }

        // 优先 host；UDP/无 SNI 时退回 sniffHost，再退回目标 IP
        var host = meta.host.IsNotEmpty() ? meta.host
            : meta.sniffHost.IsNotEmpty() ? meta.sniffHost
            : meta.destinationIP;

        if (host.IsNullOrEmpty())
        {
            return null;
        }

        // 端口后缀去掉，同一域名不同连接才能归到一起
        var idx = host!.LastIndexOf(':');
        if (idx > 0 && host.IndexOf(':') == idx)
        {
            host = host[..idx];
        }

        return host.Trim().ToLowerInvariant();
    }

    private void MaybeFlush()
    {
        if (DateTime.Now - _lastFlush < FlushInterval)
        {
            return;
        }

        Flush();
    }

    /// <summary>
    /// 排一次落库（不阻塞）。写库放到锁外异步做 —— 这个服务会在 UI 线程上
    /// 被调用（切到「按域名」要取数），持锁同步等 SQLite 会让界面卡住。
    /// </summary>
    public void Flush()
    {
        _ = FlushAsync();
    }

    public async Task FlushAsync()
    {
        Dictionary<string, (long Up, long Down)> snapshot;
        lock (_lock)
        {
            _lastFlush = DateTime.Now;
            if (_pending.Count == 0)
            {
                return;
            }

            snapshot = new Dictionary<string, (long, long)>(_pending);
            _pending.Clear();
        }

        try
        {
            foreach (var (key, delta) in snapshot)
            {
                var table = SQLiteHelper.Instance.TableAsync<DomainStatItem>();
                var row = await table.Where(t => t.Key == key).FirstOrDefaultAsync();
                if (row is null)
                {
                    await SQLiteHelper.Instance.ReplaceAsync(new DomainStatItem
                    {
                        Key = key,
                        Up = delta.Up,
                        Down = delta.Down,
                    });
                }
                else
                {
                    row.Up += delta.Up;
                    row.Down += delta.Down;
                    await SQLiteHelper.Instance.ReplaceAsync(row);
                }
            }
        }
        catch (Exception ex)
        {
            // 写失败把这批增量放回内存，下一轮重试
            lock (_lock)
            {
                foreach (var (key, delta) in snapshot)
                {
                    var cur = _pending.TryGetValue(key, out var p) ? p : (0L, 0L);
                    _pending[key] = (cur.Item1 + delta.Up, cur.Item2 + delta.Down);
                }
            }

            Logging.SaveLog($"{_tag}.Flush", ex);
        }
    }

    /// <summary>最近 N 天按域名聚合，降序返回。</summary>
    public async Task<List<DomainStatItem>> GetRangeAsync(int days)
    {
        await FlushAsync();

        try
        {
            var from = DateTime.Now.AddDays(-(days - 1)).ToString("yyyyMMdd");
            var table = SQLiteHelper.Instance.TableAsync<DomainStatItem>();
            var rows = await table.Where(t => t.Key != null).ToListAsync();

            var map = new Dictionary<string, DomainStatItem>();
            foreach (var row in rows)
            {
                var sep = row.Key.IndexOf('|');
                if (sep <= 0)
                {
                    continue;
                }

                var day = row.Key[..sep];
                if (string.CompareOrdinal(day, from) < 0)
                {
                    continue;
                }

                var host = row.Key[(sep + 1)..];
                if (!map.TryGetValue(host, out var agg))
                {
                    agg = new DomainStatItem { Key = host };
                    map[host] = agg;
                }

                agg.Up += row.Up;
                agg.Down += row.Down;
            }

            return map.Values
                .OrderByDescending(t => t.Down + t.Up)
                .ToList();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.GetRange", ex);
            return [];
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            await SQLiteHelper.Instance.ExecuteAsync("delete from DomainStatItem");
            lock (_lock)
            {
                _pending.Clear();
                _seen.Clear();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.Clear", ex);
        }
    }

    private void ResetSeen()
    {
        lock (_lock)
        {
            _seen.Clear();
        }
    }
}
