using System.Text.Json;

namespace ServiceLib.Services;

/// <summary>
/// 按进程统计**走代理**的流量。
///
/// 数据来源是 clash API 的 /connections：每条连接带 metadata.sourcePort 与
/// 精确的 upload/download。把 sourcePort 映射到 PID（<see cref="PortProcessResolver"/>）
/// 就得到"这个程序的这条连接用了多少字节"。
///
/// 为什么不用 ETW：实测 Windows 内核网络事件不上报回环连接的批量数据
/// （4MB 传输时回环事件最大 52 字节），"按应用统计走代理流量"用 ETW 不成立。
/// ETW 只能在"全机流量（含直连）"这个口径下工作，而那个口径回答不了
/// "是谁在用代理"，所以这里整体换成 clash API。
///
/// 与「按域名」同源、同口径：仅代理，不含直连；内核为 xray 时无数据。
/// </summary>
public class AppStatService
{
    private static readonly string _tag = "AppStatService";
    private static readonly Lazy<AppStatService> _instance = new(() => new());
    public static AppStatService Instance => _instance.Value;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();

    /// <summary>连接 id → 上一轮读到的 (up, down)。</summary>
    private readonly Dictionary<string, ConnState> _conns = [];
    private readonly Dictionary<string, (long Up, long Down)> _pending = [];
    private DateTime _lastFlush = DateTime.MinValue;

    private CancellationTokenSource? _cts;

    /// <summary>当前内核是否支持 clash API。</summary>
    public static bool IsSupported => AppManager.Instance.RunningCoreType is ECoreType.mihomo or ECoreType.sing_box;

    public static string UnsupportedHint => AppManager.Instance.RunningCoreType switch
    {
        ECoreType.mihomo or ECoreType.sing_box => string.Empty,
        ECoreType.Xray or ECoreType.v2fly or ECoreType.v2fly_v5 =>
            "按应用统计与按域名同源，依赖 clash API；当前 Xray 内核不提供。切到 sing_box 或 mihomo 后可用。",
        _ => "内核未运行。连接到 sing_box 或 mihomo 内核的节点后即可按应用统计。",
    };

    public void Start()
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
                        // 换内核后连接 id 全换了一批，旧快照留着会把"新连接"重复计入
                        ResetSeen();
                        continue;
                    }

                    var raw = await HttpClientHelper.Instance
                        .TryGetAsync($"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort2}/connections", token)
                        .ConfigureAwait(false);
                    if (raw.IsNullOrEmpty())
                    {
                        continue;
                    }

                    Accumulate(raw!);
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

    /// <summary>每条连接的追踪状态：端口、上次累计值、尚未归因到进程的字节。</summary>
    private sealed class ConnState
    {
        public int Port;
        public long LastUp;
        public long LastDown;
        public long PendingUp;
        public long PendingDown;
    }

    private void Accumulate(string raw)
    {
        var list = ClashConnectionsParser.Parse(raw);
        if (list.Count == 0)
        {
            return;
        }

        var today = DateTime.Now.ToString("yyyyMMdd");
        var alive = new HashSet<string>();

        foreach (var conn in list)
        {
            alive.Add(conn.Id);

            ConnState state;
            if (!_conns.TryGetValue(conn.Id, out state!))
            {
                // 第一次见到这条连接：把已有字节记为待归因（不是丢弃），
                // 端口通常下一轮就能解析到进程名
                state = new ConnState
                {
                    Port = conn.SourcePort,
                    LastUp = 0,
                    LastDown = 0,
                    PendingUp = conn.Up,
                    PendingDown = conn.Down,
                };
                _conns[conn.Id] = state;
            }

            var dUp = conn.Up >= state.LastUp ? conn.Up - state.LastUp : conn.Up;
            var dDown = conn.Down >= state.LastDown ? conn.Down - state.LastDown : conn.Down;
            state.LastUp = conn.Up;
            state.LastDown = conn.Down;
            if (state.Port == 0)
            {
                state.Port = conn.SourcePort;
            }

            state.PendingUp += dUp;
            state.PendingDown += dDown;

            if (state.PendingUp == 0 && state.PendingDown == 0)
            {
                continue;
            }

            var name = PortProcessResolver.Instance.Resolve(state.Port);
            if (name.IsNullOrEmpty())
            {
                // 还解析不到进程：先留在这条连接上，下一轮再试（早期版本直接丢弃，
                // 实测 4MB 只记到 195KB，就是这段被丢掉的）
                continue;
            }

            var key = $"{today}|{name}";
            lock (_lock)
            {
                var cur = _pending.TryGetValue(key, out var p) ? p : (0L, 0L);
                _pending[key] = (cur.Item1 + state.PendingUp, cur.Item2 + state.PendingDown);
            }

            state.PendingUp = 0;
            state.PendingDown = 0;
        }

        // 连接断开：它上面的待归因字节再试一次，然后丢弃状态
        foreach (var id in _conns.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            var state = _conns[id];
            if (state.PendingUp > 0 || state.PendingDown > 0)
            {
                var name = PortProcessResolver.Instance.Resolve(state.Port);
                if (name.IsNotEmpty())
                {
                    var key = $"{today}|{name}";
                    lock (_lock)
                    {
                        var cur = _pending.TryGetValue(key, out var p) ? p : (0L, 0L);
                        _pending[key] = (cur.Item1 + state.PendingUp, cur.Item2 + state.PendingDown);
                    }
                }
            }

            _conns.Remove(id);
        }
    }

    private void MaybeFlush()
    {
        if (DateTime.Now - _lastFlush < FlushInterval)
        {
            return;
        }

        Flush();
    }

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
                var table = SQLiteHelper.Instance.TableAsync<AppStatItem>();
                var row = await table.Where(t => t.Key == key).FirstOrDefaultAsync();
                if (row is null)
                {
                    await SQLiteHelper.Instance.ReplaceAsync(new AppStatItem
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

    /// <summary>最近 N 天按进程聚合，降序返回。Key 为进程名。</summary>
    public async Task<List<AppStatItem>> GetRangeAsync(int days)
    {
        await FlushAsync();

        try
        {
            var from = DateTime.Now.AddDays(-(days - 1)).ToString("yyyyMMdd");
            var rows = await SQLiteHelper.Instance.TableAsync<AppStatItem>().ToListAsync();

            var map = new Dictionary<string, AppStatItem>();
            foreach (var row in rows)
            {
                var sep = row.Key.IndexOf('|');
                if (sep <= 0 || string.CompareOrdinal(row.Key[..sep], from) < 0)
                {
                    continue;
                }

                var name = row.Key[(sep + 1)..];
                if (!map.TryGetValue(name, out var agg))
                {
                    agg = new AppStatItem { Key = name };
                    map[name] = agg;
                }

                agg.Up += row.Up;
                agg.Down += row.Down;
            }

            return map.Values.OrderByDescending(t => t.Down + t.Up).ToList();
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
            await SQLiteHelper.Instance.ExecuteAsync("delete from AppStatItem");
            lock (_lock)
            {
                _pending.Clear();
                _conns.Clear();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.Clear", ex);
        }
    }

    private void ResetSeen() => _conns.Clear();

    public string StatusHint => UnsupportedHint;
}

/// <summary>把 clash API 的 /connections 解析成归因所需的最小字段。</summary>
public static class ClashConnectionsParser
{
    public readonly record struct Conn(string Id, int SourcePort, long Up, long Down);

    public static List<Conn> Parse(string? json)
    {
        var result = new List<Conn>();
        if (json.IsNullOrEmpty())
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(json!);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("connections", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id.IsNullOrEmpty())
                {
                    continue;
                }

                var port = 0;
                if (item.TryGetProperty("metadata", out var meta)
                    && meta.ValueKind == JsonValueKind.Object
                    && meta.TryGetProperty("sourcePort", out var sp))
                {
                    var text = sp.ValueKind switch
                    {
                        JsonValueKind.String => sp.GetString(),
                        JsonValueKind.Number => sp.GetRawText(),
                        _ => null,
                    };
                    if (text is not null)
                    {
                        int.TryParse(text, out port);
                    }
                }

                result.Add(new Conn(
                    id!,
                    port,
                    ReadLong(item, "upload"),
                    ReadLong(item, "download")));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return result;
    }

    private static long ReadLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el))
        {
            return 0;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var v) => v,
            JsonValueKind.String when long.TryParse(el.GetString(), out var v) => v,
            _ => 0,
        };
    }
}
