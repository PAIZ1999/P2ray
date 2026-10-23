using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace ServiceLib.Services;

/// <summary>
/// 按进程统计流量，数据来自 ETW 的内核网络事件。
///
/// 为什么必须用 ETW：xray / sing-box 的统计都只有 inbound / outbound / 域名维度，
/// 没有任何办法知道"是哪个程序"。Windows 也只有 ETW 的内核网络事件带 PID。
///
/// 三条硬性约束，界面必须如实标注：
/// 1. 需要管理员权限 —— 内核 provider 不是普通用户能订阅的；
/// 2. 统计的是**全机流量**，含未经代理的直连，与「概览」页口径不同；
/// 3. 只能拿到目标 IP 与端口，所以这里按**进程**聚合，不按域名。
/// </summary>
public class AppStatService
{
    private static readonly string _tag = "AppStatService";
    private static readonly Lazy<AppStatService> _instance = new(() => new());
    public static AppStatService Instance => _instance.Value;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(20);
    private const string SessionName = "P2ray-Kernel-Network";

    private readonly object _lock = new();
    private readonly Dictionary<int, string> _pidNames = [];
    private readonly Dictionary<string, (long Up, long Down)> _pending = [];
    private DateTime _lastFlush = DateTime.MinValue;

    private TraceEventSession? _session;
    private Thread? _thread;
    private volatile bool _running;
    private long _seenEvents;   // TEMPDBG
    private long _matchedEvents;   // TEMPDBG
    private string? _lastError;

    /// <summary>是否具备运行条件（管理员）。</summary>
    public static bool IsSupported
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static string UnsupportedHint => IsSupported
        ? string.Empty
        : "按应用统计需要管理员权限才能订阅内核网络事件。请以管理员身份重启 P2ray 后可用。";

    /// <summary>启动 ETW 会话。重复调用是安全的。</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        if (!IsSupported)
        {
            _lastError = UnsupportedHint;
            return;
        }

        try
        {
            // 清掉同名残留会话：进程被强杀时 ETW 会话会留在系统里（实测 Running），
            // 之后新实例再订阅就收不到任何事件 —— 表现是按应用永远是空的。
            StopStaleSession();

            _session = new TraceEventSession(SessionName) { StopOnDispose = true };
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            _session.Source.Kernel.TcpIpRecv += data => Accumulate(data.ProcessID, 0, data.size, data.saddr?.ToString(), data.sport);
            _session.Source.Kernel.TcpIpSend += data => Accumulate(data.ProcessID, data.size, 0, data.daddr?.ToString(), data.dport);

            _running = true;
            _thread = new Thread(() =>
            {
                try
                {
                    _session?.Source.Process();
                }
                catch (Exception ex)
                {
                    // 会话被系统回收（例如同名会话已存在）时这里会抛，
                    // 记下来，界面据此说明而不是静默失效。
                    _lastError = ex.Message;
                    Logging.SaveLog($"{_tag}.Process", ex);
                }
                finally
                {
                    _running = false;
                }
            })
            {
                IsBackground = true,
                Name = "P2ray-EtwNetwork",
            };
            _thread.Start();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Logging.SaveLog($"{_tag}.Start", ex);
            _running = false;
        }
    }

    public void Close()
    {
        try
        {
            _running = false;
            _session?.Dispose();
            _session = null;
            Flush();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.Close", ex);
        }
    }

    /// <summary>
    /// 是否只统计走代理的流量。
    ///
    /// 判定依据很简单：走系统代理的程序，流量一定先发到本地代理端口
    /// （应用 → 127.0.0.1:端口），回程同理。所以筛出「对端是回环地址且端口等于
    /// 本地代理端口」的事件，就正好是经代理的那部分。
    /// TUN 模式没有这段回环握手，此时该模式拿不到数据，需要提示用户。
    /// </summary>
    public bool ProxyOnly { get; set; } = true;

    /// <summary>诊断用：ETW 收到的原始事件数（自检读取，不影响统计）。</summary>
    public long DebugSeenEvents => _seenEvents;

    /// <summary>诊断用：通过回环代理端口过滤后的事件数。</summary>
    public long DebugMatchedEvents => _matchedEvents;

    private static int ProxyPort()
    {
        try
        {
            return AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsLoopback(string? addr)
        => addr is not null
           && (addr == "127.0.0.1" || addr == "::1" || addr.StartsWith("127.", StringComparison.Ordinal));

    /// <summary>ETW 回调里不能做重活：只做内存加法。</summary>
    private void Accumulate(int pid, long up, long down, string? addr, int port)
    {
        _seenEvents++;   // TEMPDBG
        if (ProxyOnly)
        {
            var proxyPort = ProxyPort();
            if (proxyPort == 0 || port != proxyPort || !IsLoopback(addr))
            {
                return;
            }
        }
        if (pid <= 0 || (up <= 0 && down <= 0))
        {
            return;
        }

        var name = ResolveName(pid);
        if (name.IsNullOrEmpty())
        {
            return;
        }

        _matchedEvents++;   // TEMPDBG
        var key = $"{(ProxyOnly ? "p" : "a")}|{DateTime.Now:yyyyMMdd}|{name}";
        var needFlush = false;
        lock (_lock)
        {
            var cur = _pending.TryGetValue(key, out var p) ? p : (0L, 0L);
            _pending[key] = (cur.Item1 + up, cur.Item2 + down);

            if (DateTime.Now - _lastFlush >= FlushInterval)
            {
                needFlush = true;
            }
        }

        // 锁外排落库：ETW 回调在热路径上，不能在这里做 IO；
        // 同时也不能让 UI 线程取数据时抢不到锁。
        if (needFlush)
        {
            _ = FlushAsync();
        }
    }

    /// <summary>PID → 进程名。缓存起来：每包都查一次进程表会很慢。</summary>
    private string? ResolveName(int pid)
    {
        lock (_lock)
        {
            if (_pidNames.TryGetValue(pid, out var cached))
            {
                return cached;
            }
        }

        string name;
        try
        {
            // 系统进程（pid 0/4）与已退出的进程直接跳过
            using var proc = Process.GetProcessById(pid);
            name = proc.ProcessName;
        }
        catch
        {
            return null;
        }

        if (name.IsNullOrEmpty())
        {
            return null;
        }

        lock (_lock)
        {
            _pidNames[pid] = name;
        }

        return name;
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
                        Up = delta.Item1,
                        Down = delta.Item2,
                    });
                }
                else
                {
                    row.Up += delta.Item1;
                    row.Down += delta.Item2;
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
                    _pending[key] = (cur.Item1 + delta.Item1, cur.Item2 + delta.Item2);
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
            var modePrefix = ProxyOnly ? "p|" : "a|";
            var table = SQLiteHelper.Instance.TableAsync<AppStatItem>();
            var rows = await table.Where(t => t.Key != null).ToListAsync();

            var map = new Dictionary<string, AppStatItem>();
            foreach (var row in rows)
            {
                // key 形如 "p|20261023|chrome"：第一段是口径，第二段是日期
                var parts = row.Key.Split('|', 3);
                if (parts.Length != 3
                    || !string.Equals(parts[0], modePrefix[..1], StringComparison.Ordinal)
                    || string.CompareOrdinal(parts[1], from) < 0)
                {
                    continue;
                }

                var name = parts[2];
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
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.Clear", ex);
        }
    }

    /// <summary>运行状态说明：空串表示正常。</summary>
    public string StatusHint => !IsSupported ? UnsupportedHint : (_lastError ?? string.Empty);

    /// <summary>停掉上次运行残留的同名 ETW 会话（logman 在 Windows 上自带）。</summary>
    private static void StopStaleSession()
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "logman.exe",
                Arguments = $"stop {SessionName} -ets",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            proc?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.StopStaleSession", ex);
        }
    }
}
