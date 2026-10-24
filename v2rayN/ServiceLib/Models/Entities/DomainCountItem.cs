namespace ServiceLib.Models.Entities;

/// <summary>
/// Xray 内核下的按域名**连接次数**（不是字节）。
///
/// 为什么单独一张表：Xray 没有 clash API，access log 只给「有这条连接」，不给字节数。
/// 把它塞进 DomainStatItem 的 Up/Down 会让单位混淆（一处是字节、一处是次数），
/// 所以另开一张表，界面上按内核切换口径并明示。
///
/// Key 与 DomainStatItem 同构（yyyyMMdd|host），方便按日期前缀清理。
/// </summary>
[Serializable]
public class DomainCountItem
{
    /// <summary>形如 "20261024|www.google.com"。</summary>
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    /// <summary>该域名当天的连接次数。</summary>
    public long Count { get; set; }
}
