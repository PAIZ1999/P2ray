namespace ServiceLib.Models.Entities;

/// <summary>
/// 按域名聚合的代理流量。
///
/// Key 里带日期（yyyyMMdd|host）：域名统计只需要最近几天，
/// 用日期前缀做主键，清理历史时按前缀删即可，不必遍历时间戳。
/// </summary>
[Serializable]
public class DomainStatItem
{
    /// <summary>形如 "20261023|www.google.com"。</summary>
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public long Up { get; set; }

    public long Down { get; set; }
}
