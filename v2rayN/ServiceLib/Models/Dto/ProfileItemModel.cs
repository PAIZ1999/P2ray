namespace ServiceLib.Models.Dto;

[Serializable]
public partial class ProfileItemModel : ReactiveObject
{
    public bool IsActive { get; set; }
    public string IndexId { get; set; }
    public EConfigType ConfigType { get; set; }
    public string Remarks { get; set; }
    public string Address { get; set; }
    public int Port { get; set; }
    public string Network { get; set; }
    public string StreamSecurity { get; set; }
    public string Subid { get; set; }
    public string SubRemarks { get; set; }
    public int Sort { get; set; }

    [Reactive]
    public partial int Delay { get; set; }

    public decimal Speed { get; set; }

    [Reactive]
    public partial string DelayVal { get; set; }

    [Reactive]
    public partial string SpeedVal { get; set; }

    [Reactive]
    public partial string IpInfo { get; set; }

    [Reactive]
    public partial string TodayUp { get; set; }

    [Reactive]
    public partial string TodayDown { get; set; }

    [Reactive]
    public partial string TotalUp { get; set; }

    [Reactive]
    public partial string TotalDown { get; set; }

    /// <summary>
    /// 今日流量，形如 "12.3 MB / 45.6 MB"（上传 / 下载）。
    ///
    /// 上游把上下行拆成 TodayUp / TodayDown 两个字段，界面上分两列显示会挤掉
    /// 分组和地址的空间。这里合成一格，未统计过时给破折号 ——
    /// 直接绑空字符串会留下一个看不出含义的空白格。
    /// </summary>
    public string TodayTrafficText => Format(TodayUp, TodayDown);

    /// <summary>累计流量，格式同 TodayTrafficText。</summary>
    public string TotalTrafficText => Format(TotalUp, TotalDown);

    private static string Format(string? up, string? down)
    {
        var hasUp = !string.IsNullOrEmpty(up);
        var hasDown = !string.IsNullOrEmpty(down);

        return (hasUp, hasDown) switch
        {
            (true, true) => $"{up} / {down}",
            (true, false) => up!,
            (false, true) => $"0 / {down}",
            _ => "—",
        };
    }

    public string GetSummary()
    {
        var summary = $"[{ConfigType}] {Remarks}";
        if (!ConfigType.IsComplexType())
        {
            summary += $"({Address}:{Port})";
        }

        return summary;
    }
}
