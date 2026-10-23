using System.Text.Json;

namespace ServiceLib.Services;

/// <summary>
/// 流量累计值的解析与差值计算。
///
/// 这部分逻辑原本埋在 <see cref="TrafficStatService"/> 的轮询循环里，
/// 既没法测、也不好排查"为什么概览是 0"。抽出来之后：
///   - 解析负责把两种内核的返回都归一成 (up, down)；
///   - Tracker 负责把累计值变成增量，并处理计数归零。
/// </summary>
public static class TrafficTotalsParser
{
    /// <summary>
    /// 解析 clash API 的 /connections 返回。
    /// sing-box 与 mihomo 都提供 uploadTotal / downloadTotal（自内核启动起的累计值）。
    /// </summary>
    public static (long Up, long Down)? ParseClashTotals(string? json)
    {
        if (json.IsNullOrEmpty())
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json!);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("uploadTotal", out var upEl)
                || !doc.RootElement.TryGetProperty("downloadTotal", out var downEl))
            {
                return null;
            }

            if (!TryReadLong(upEl, out var up) || !TryReadLong(downEl, out var down))
            {
                return null;
            }

            return (up, down);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadLong(JsonElement el, out long value)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number when el.TryGetInt64(out value):
                return true;
            case JsonValueKind.String when long.TryParse(el.GetString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
}

/// <summary>
/// 把"自内核启动以来的累计值"转换成"本次新增"。
///
/// 内核重启后累计值会从 0 重新开始，这时不能把负数差额记成流量，
/// 而是把基线重置到当前值。
/// </summary>
public sealed class TrafficDeltaTracker
{
    private bool _initialized;
    private long _lastUp;
    private long _lastDown;

    /// <summary>喂入一次累计读数，返回本次增量。首次读数与计数回退都返回 (0,0)。</summary>
    public (long Up, long Down) Feed(long up, long down)
    {
        if (up < 0 || down < 0)
        {
            return (0, 0);
        }

        if (!_initialized)
        {
            _initialized = true;
            _lastUp = up;
            _lastDown = down;
            return (0, 0);
        }

        if (up < _lastUp || down < _lastDown)
        {
            _lastUp = up;
            _lastDown = down;
            return (0, 0);
        }

        var dUp = up - _lastUp;
        var dDown = down - _lastDown;
        _lastUp = up;
        _lastDown = down;
        return (dUp, dDown);
    }

    public void Reset()
    {
        _initialized = false;
        _lastUp = 0;
        _lastDown = 0;
    }
}
