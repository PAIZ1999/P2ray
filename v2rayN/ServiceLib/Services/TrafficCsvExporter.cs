using System.Globalization;
using System.Text;

namespace ServiceLib.Services;

/// <summary>
/// 把统计数据导出成 CSV。
///
/// 只输出「字节数」这一个权威字段，另外给一列人类可读值方便肉眼核对：
/// 用户拿去做月报时按字节自己算总量，不会被四舍五入的 "1.2 MB" 坑到。
/// </summary>
public static class TrafficCsvExporter
{
    /// <summary>导出用的一行：名称（日期或域名/进程）+ 上行/下行字节。</summary>
    public readonly record struct Row(string Name, long Up, long Down);

    /// <summary>
    /// 生成 CSV 文本。第一列名称、随后是字节数与可读值。
    /// </summary>
    public static string Build(IEnumerable<Row> rows, string nameHeader = "name")
    {
        var sb = new StringBuilder();
        sb.Append(nameHeader).Append(",down_bytes,up_bytes,down,up").Append('\n');

        foreach (var row in rows)
        {
            sb.Append(Escape(row.Name)).Append(',')
              .Append(row.Down.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(row.Up.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Escape(FormatBytes(row.Down))).Append(',')
              .Append(Escape(FormatBytes(row.Up))).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// CSV 字段转义：含逗号、引号或换行时用双引号包起来，内部引号翻倍。
    /// 域名里不会有逗号，但进程名和未来的备注可能有。
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value.ToString("F0", CultureInfo.InvariantCulture)} {units[unit]}"
            : $"{value.ToString("F1", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
