namespace ServiceLib.Models.Dto;

/// <summary>
/// 一组站点探测结果的聚合，用于首页按"国际 / AI / 国内"分组展示。
///
/// 单独成类而不是直接在界面分组：每组标题上要显示本组的可用数与平均延迟，
/// 这些聚合值必须在数据层算好，XAML 的 CollectionView 分组拿不到。
/// </summary>
public class NetProbeGroup
{
    /// <summary>分组名：国际 / AI / 国内。</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>本组站点。</summary>
    public BulkObservableCollection<NetProbeResult> Items { get; set; } = [];

    /// <summary>本组可用数。</summary>
    public int OkCount { get; set; }

    /// <summary>本组站点总数。</summary>
    public int TotalCount { get; set; }

    /// <summary>本组成功项的平均延迟（毫秒）。没有成功项时为 0。</summary>
    public double AverageMs { get; set; }

    /// <summary>标题右侧的小结，如 "8/8 · 平均 268ms"。</summary>
    public string SummaryText => OkCount == 0
        ? $"0/{TotalCount} 可用"
        : AverageMs > 0
            ? $"{OkCount}/{TotalCount} 可用 · 平均 {AverageMs:F0}ms"
            : $"{OkCount}/{TotalCount} 可用";
}
