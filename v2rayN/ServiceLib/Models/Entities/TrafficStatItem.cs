namespace ServiceLib.Models.Entities;

/// <summary>
/// 按小时聚合的代理流量。
///
/// 为什么按小时存：用户要看的是"各个时段用了多少"，日/周/月都能由小时桶加出来，
/// 反过来则不行 —— 只存日总计就没法回答"晚上 8 点是不是特别费流量"。
/// 每小时一行，一天 24 行，一年不到 9000 行，体积可以忽略。
/// </summary>
[Serializable]
public class TrafficStatItem
{
    /// <summary>这一小时起点的本地时间 Ticks，按小时对齐，同时作为主键。</summary>
    [PrimaryKey]
    public long HourTick { get; set; }

    /// <summary>该小时内累计上行字节。</summary>
    public long Up { get; set; }

    /// <summary>该小时内累计下行字节。</summary>
    public long Down { get; set; }
}
