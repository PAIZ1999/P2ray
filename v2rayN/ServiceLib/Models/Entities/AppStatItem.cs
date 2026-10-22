namespace ServiceLib.Models.Entities;

/// <summary>
/// 按进程聚合的流量。
///
/// 口径与「概览」「按域名」都不同：这里统计的是**全机流量（含直连）**，
/// 因为 ETW 的内核网络事件不区分某个连接是否经过代理。界面上必须写清楚，
/// 否则两份数字对不上会被当成 bug。
///
/// Key 用 "yyyyMMdd|进程名"：进程名比 PID 稳定（PID 每次运行都会变）。
/// </summary>
[Serializable]
public class AppStatItem
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public long Up { get; set; }

    public long Down { get; set; }
}
