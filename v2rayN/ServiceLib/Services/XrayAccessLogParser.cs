using System.Text.RegularExpressions;

namespace ServiceLib.Services;

/// <summary>
/// 解析 Xray 的 access log 行，取出「目标域名」。
///
/// Xray 没有 clash API，拿不到逐连接字节；能拿到域名的只有 access log（loglevel=info）。
/// 实测样例行：
///   2026/10/24 17:05:12.323959 from tcp:127.0.0.1:11498 accepted tcp:speed.cloudflare.com:443 [socks >> proxy]
/// 注意箭头有 "-&gt;" 和 "&gt;&gt;" 两种写法，正则不能只认其中一种。
/// </summary>
public static partial class XrayAccessLogParser
{
    [GeneratedRegex(
        @"^(?<time>\d{4}/\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?)\s+from\s+\S+?:(?<srcport>\d+)\s+accepted\s+\w+:(?<host>[^\s:\[]+):(?<port>\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    /// <summary>取目标域名；不是连接行（比如启动横幅、错误行）返回 null。</summary>
    public static string? ParseHost(string? line)
    {
        if (line.IsNullOrEmpty())
        {
            return null;
        }

        var m = LineRegex().Match(line!);
        if (!m.Success)
        {
            return null;
        }

        var host = m.Groups["host"].Value;
        // 纯 IP 的目标没有"域名"意义，但也算一次连接，保留原样
        return host.IsNullOrEmpty() ? null : host;
    }

    /// <summary>取来源端口（用于将来做端口→进程归因；当前只存域名计数）。</summary>
    public static int? ParseSourcePort(string? line)
    {
        if (line.IsNullOrEmpty())
        {
            return null;
        }

        var m = LineRegex().Match(line!);
        return m.Success && int.TryParse(m.Groups["srcport"].Value, out var port) ? port : null;
    }

    /// <summary>取该行的时间戳；不是连接行返回 null。</summary>
    public static DateTime? ParseTime(string? line)
    {
        if (line.IsNullOrEmpty())
        {
            return null;
        }

        var m = LineRegex().Match(line!);
        if (!m.Success)
        {
            return null;
        }

        var text = m.Groups["time"].Value;
        return DateTime.TryParseExact(text, ["yyyy/MM/dd HH:mm:ss.ffffff", "yyyy/MM/dd HH:mm:ss.fff", "yyyy/MM/dd HH:mm:ss"],
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt)
            ? dt
            : null;
    }
}