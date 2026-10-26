namespace ServiceLib.Services;

/// <summary>
/// Xray 内核下的「按域名」：读 access log，统计**连接次数**。
///
/// 依赖两个前提，缺一不可：
///   1. 用户在设置里打开了日志（`CoreBasicItem.LogEnabled`），Xray 才会写 access log；
///   2. 日志级别为 info，否则不记录 accepted 行。
/// 任一不满足时本服务返回空，界面显示原因（不是空表格）。
///
/// 只做计数、不做字节：Xray 的 stats API 只有入站/出站/用户粒度，没有逐连接字节。
/// 字节口径见 ADR-0001（clash 内核）。
/// </summary>
public class XrayDomainStatService
{
    private static readonly string _tag = "XrayDomainStatService";
    private static readonly Lazy<XrayDomainStatService> _instance = new(() => new());
    public static XrayDomainStatService Instance => _instance.Value;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly object _lock = new();
    private readonly Dictionary<string, long> _pending = [];

    /// <summary>已读到的文件位置：文件路径 → 字节偏移。跨天换文件时按路径分别记。</summary>
    private readonly Dictionary<string, long> _offsets = [];

    private CancellationTokenSource? _cts;

    /// <summary>
    /// 偏移量落盘。必须在进程间保持：否则每次重启都会从头重读 access log，
    /// 已入库的计数被重复累加（实测 3 次启动把 3 条连接记成 18 条）。
    /// </summary>
    private static string OffsetPath => Path.Combine(Utils.GetConfigPath(""), "xray-access-offset.json");

    private bool _offsetsLoaded;

    /// <summary>只统计这个时刻之后写入的日志行（从日志里解析出的时间戳判断）。</summary>
    private readonly DateTime _countSince = DateTime.Now.AddMinutes(-2);

    /// <summary>当前内核是 Xray 且日志开着，才有数据。</summary>
    public static bool IsSupported =>
        AppManager.Instance.RunningCoreType is ECoreType.Xray or ECoreType.v2fly or ECoreType.v2fly_v5;

    public static string UnsupportedHint => IsSupported
        ? (AppManager.Instance.Config.CoreBasicItem.LogEnabled
            ? string.Empty
            : "Xray 的按域名统计依赖 access log：请在「设置 → 日志」打开日志开关（级别 info）。")
        : "该统计依赖 Xray 的 access log；当前内核不是 Xray。";

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
                    if (!IsSupported || !AppManager.Instance.Config.CoreBasicItem.LogEnabled)
                    {
                        continue;
                    }

                    await PollOnceAsync();
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
            // 退出路径不该抛
        }
    }

    /// <summary>读一次日志的新增部分，累加到当天计数。</summary>
    private async Task PollOnceAsync()
    {
        var path = Utils.GetLogPath($"Vaccess_{DateTime.Now:yyyy-MM-dd}.txt");
        if (!File.Exists(path))
        {
            return;
        }

        LoadOffsetsOnce(path);

        string chunk;
        lock (_lock)
        {
            var offset = _offsets.TryGetValue(path, out var o) ? o : 0L;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= offset)
            {
                // 文件被截断或没新增
                _offsets[path] = fs.Length;
                return;
            }

            fs.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            chunk = reader.ReadToEnd();
            _offsets[path] = fs.Length;
        }

        var today = DateTime.Now.ToString("yyyyMMdd");
        var touched = new Dictionary<string, long>();

        foreach (var line in chunk.Split((char)10))
        {
            var host = XrayAccessLogParser.ParseHost(line);
            if (host.IsNullOrEmpty())
            {
                continue;
            }

            // 旧行（本次启动之前写入的）不计数，避免首次启用日志时把历史连接也算进来
            var lineTime = XrayAccessLogParser.ParseTime(line);
            if (lineTime is not null && lineTime < _countSince)
            {
                continue;
            }

            var key = $"{today}|{host}";
            touched[key] = touched.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        SaveOffsets();

        if (touched.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var (key, count) in touched)
            {
                _pending[key] = (_pending.TryGetValue(key, out var p) ? p : 0) + count;
            }
        }

        await FlushAsync();
    }

    public async Task FlushAsync()
    {
        Dictionary<string, long> snapshot;
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            snapshot = new Dictionary<string, long>(_pending);
            _pending.Clear();
        }

        try
        {
            foreach (var (key, count) in snapshot)
            {
                var table = SQLiteHelper.Instance.TableAsync<DomainCountItem>();
                var row = await table.Where(t => t.Key == key).FirstOrDefaultAsync();
                if (row is null)
                {
                    await SQLiteHelper.Instance.ReplaceAsync(new DomainCountItem { Key = key, Count = count });
                }
                else
                {
                    row.Count += count;
                    await SQLiteHelper.Instance.ReplaceAsync(row);
                }
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                foreach (var (key, count) in snapshot)
                {
                    _pending[key] = (_pending.TryGetValue(key, out var p) ? p : 0) + count;
                }
            }

            Logging.SaveLog($"{_tag}.Flush", ex);
        }
    }

    /// <summary>最近 N 天按域名聚合的连接次数，降序返回。</summary>
    public async Task<List<DomainCountItem>> GetRangeAsync(int days)
    {
        await FlushAsync();

        try
        {
            var from = DateTime.Now.AddDays(-(days - 1)).ToString("yyyyMMdd");
            var rows = await SQLiteHelper.Instance.TableAsync<DomainCountItem>().ToListAsync();

            var map = new Dictionary<string, long>();
            foreach (var row in rows)
            {
                var sep = row.Key.IndexOf('|');
                if (sep <= 0 || string.CompareOrdinal(row.Key[..sep], from) < 0)
                {
                    continue;
                }

                var host = row.Key[(sep + 1)..];
                map[host] = (map.TryGetValue(host, out var c) ? c : 0) + row.Count;
            }

            return map
                .OrderByDescending(t => t.Value)
                .Select(t => new DomainCountItem { Key = t.Key, Count = t.Value })
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
            await SQLiteHelper.Instance.ExecuteAsync("delete from DomainCountItem");
            lock (_lock)
            {
                _pending.Clear();
                _offsets.Clear();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.Clear", ex);
        }
    }

    /// <summary>首次使用某个日志文件时，从文件末尾开始（不重算历史），并把偏移持久化。</summary>
    private void LoadOffsetsOnce(string path)
    {
        if (_offsetsLoaded)
        {
            return;
        }

        _offsetsLoaded = true;
        try
        {
            if (File.Exists(OffsetPath))
            {
                var json = File.ReadAllText(OffsetPath);
                var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(json);
                if (saved is not null)
                {
                    foreach (var (k, v) in saved)
                    {
                        _offsets[k] = v;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.LoadOffsets", ex);
        }

        if (!_offsets.ContainsKey(path))
        {
            // 没有记录：从 0 读，但只统计"本次启动之后"的行（见 _countSince）。
            // 早期版本这里取文件末尾，结果第一次打开日志时**已有行全被跳过** ——
            // 用户看到的就是"我打开了 info 还是没有数据"。
            _offsets[path] = 0;
            SaveOffsets();
        }
    }

    private void SaveOffsets()
    {
        try
        {
            lock (_lock)
            {
                File.WriteAllText(OffsetPath, System.Text.Json.JsonSerializer.Serialize(_offsets));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.SaveOffsets", ex);
        }
    }
}
