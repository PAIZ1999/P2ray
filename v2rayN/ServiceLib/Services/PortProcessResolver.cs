using System.Diagnostics;

namespace ServiceLib.Services;

/// <summary>
/// 端口 → 进程名的映射，用来把 clash API 里的 sourcePort 归因到具体程序。
///
/// 为什么不用 ETW：实测 Windows 内核网络事件**不上报回环连接的批量数据**
/// （4MB 传输时回环事件最大只有 52 字节），所以"按应用统计走代理的流量"
/// 用 ETW 根本不成立。
///
/// 做法：clash API 给出每条连接的 sourcePort 与精确字节数，这里把
/// (127.0.0.1, sourcePort) 映射到 PID，再换算进程名。数据源是 netstat，
/// 每 2 秒刷新一次并缓存 —— 端口在连接期间不会变，缓存足够用。
/// </summary>
public sealed class PortProcessResolver
{
    private static readonly Lazy<PortProcessResolver> _instance = new(() => new());
    public static PortProcessResolver Instance => _instance.Value;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly object _lock = new();
    private Dictionary<int, string> _portToName = [];
    private DateTime _lastRefresh = DateTime.MinValue;

    /// <summary>
    /// 解析 netstat -ano 的输出，提取「本地端口 → PID」。
    ///
    /// 只关心 TCP 里带 PID 的 ESTABLISHED/LISTENING 行；格式示例：
    ///   TCP    127.0.0.1:52341    127.0.0.1:11808    ESTABLISHED    1234
    /// </summary>
    public static Dictionary<int, int> ParseNetstatPorts(string? output)
    {
        var map = new Dictionary<int, int>();
        if (output.IsNullOrEmpty())
        {
            return map;
        }

        foreach (var raw in output!.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            if (!TryParsePort(parts[1], out var localPort))
            {
                continue;
            }

            if (!int.TryParse(parts[^1], out var pid) || pid <= 0)
            {
                continue;
            }

            map[localPort] = pid;
        }

        return map;
    }

    /// <summary>从 "127.0.0.1:52341" / "[::1]:52341" 里取出端口。</summary>
    public static bool TryParsePort(string endpoint, out int port)
    {
        port = 0;
        var idx = endpoint.LastIndexOf(':');
        if (idx < 0 || idx == endpoint.Length - 1)
        {
            return false;
        }

        return int.TryParse(endpoint[(idx + 1)..], out port) && port > 0;
    }

    /// <summary>按端口查进程名；查不到返回 null。</summary>
    public string? Resolve(int port)
    {
        RefreshIfStale();

        lock (_lock)
        {
            return _portToName.TryGetValue(port, out var name) ? name : null;
        }
    }

    private void RefreshIfStale()
    {
        if (DateTime.Now - _lastRefresh < RefreshInterval)
        {
            return;
        }

        lock (_lock)
        {
            if (DateTime.Now - _lastRefresh < RefreshInterval)
            {
                return;
            }

            _lastRefresh = DateTime.Now;

            try
            {
                var output = RunNetstat();
                var ports = ParseNetstatPorts(output);
                var names = new Dictionary<int, string>();

                foreach (var (port, pid) in ports)
                {
                    var name = ProcessNameOf(pid);
                    if (name.IsNotEmpty())
                    {
                        names[port] = name;
                    }
                }

                _portToName = names;
            }
            catch (Exception ex)
            {
                Logging.SaveLog("PortProcessResolver.Refresh", ex);
            }
        }
    }

    private static string? RunNetstat()
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "netstat.exe",
            Arguments = "-ano -p tcp",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        });

        if (proc is null)
        {
            return null;
        }

        var text = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(2000);
        return text;
    }

    private static string? ProcessNameOf(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            // 进程已退出，正常情况
            return null;
        }
    }
}
