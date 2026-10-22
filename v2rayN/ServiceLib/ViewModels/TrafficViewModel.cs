namespace ServiceLib.ViewModels;

/// <summary>
/// 流量统计页：按时段看上下行用量。
///
/// 数据来自 TrafficStatService 的小时桶 —— 代理流量（经内核）而不是全机流量，
/// 因为 P2ray 只能观测到自己转发的部分。这一点必须在界面上写清楚，
/// 否则用户会拿它和运营商的账单对比，然后以为程序算错了。
/// </summary>
public partial class TrafficViewModel : ReactiveObject
{
    /// <summary>柱状图的最大高度（px）。数值按当窗口内的最大值等比缩放。</summary>
    private const double BarMaxHeight = 76;

    [Reactive]
    public partial bool IsLoading { get; set; }

    [Reactive]
    public partial BulkObservableCollection<TrafficHourItem> Hours { get; set; } = [];

    [Reactive]
    public partial BulkObservableCollection<TrafficDayItem> Days { get; set; } = [];

    [Reactive]
    public partial string TodayUpText { get; set; } = "—";

    [Reactive]
    public partial string TodayDownText { get; set; } = "—";

    [Reactive]
    public partial string WeekUpText { get; set; } = "—";

    [Reactive]
    public partial string WeekDownText { get; set; } = "—";

    [Reactive]
    public partial string TotalUpText { get; set; } = "—";

    [Reactive]
    public partial string TotalDownText { get; set; } = "—";

    /// <summary>最近 24 小时的峰值，界面上作为刻度说明。</summary>
    [Reactive]
    public partial string PeakHourText { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> RefreshCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ClearCmd { get; }

    public TrafficViewModel()
    {
        RefreshCmd = ReactiveCommand.CreateFromTask(LoadAsync);
        ClearCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await TrafficStatService.Instance.ClearAsync();
            await LoadAsync();
        });

        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var hourly = await TrafficStatService.Instance.GetHourlyAsync(24);
            var daily = await TrafficStatService.Instance.GetDailyAsync(7);

            var peak = hourly.Count > 0 ? hourly.Max(t => t.Up + t.Down) : 0;

            // 柱高按窗口内峰值等比缩放：固定比例会让轻度使用的用户永远只看到一条线。
            var items = new List<TrafficHourItem>(hourly.Count);
            foreach (var row in hourly)
            {
                var hour = new DateTime(row.HourTick);
                var total = row.Up + row.Down;
                var scale = peak > 0 ? BarMaxHeight / peak : 0;
                items.Add(new TrafficHourItem
                {
                    Label = hour.Hour.ToString("D2"),
                    IsCurrent = hour.Hour == DateTime.Now.Hour && hour.Date == DateTime.Today,
                    UpText = FormatBytes(row.Up),
                    DownText = FormatBytes(row.Down),
                    UpHeight = row.Up * scale,
                    DownHeight = row.Down * scale,
                    // 空桶也给 2px 底座，避免整排柱子看起来"断了"
                    TotalHeight = Math.Max(total * scale, total > 0 ? 3 : 2),
                });
            }

            Hours = new BulkObservableCollection<TrafficHourItem>(items);

            Days = new BulkObservableCollection<TrafficDayItem>(daily
                .AsEnumerable()
                .Reverse()
                .Select(row => new TrafficDayItem
                {
                    Label = new DateTime(row.HourTick).ToString("MM-dd 周") + WeekdayZh(new DateTime(row.HourTick)),
                    UpText = FormatBytes(row.Up),
                    DownText = FormatBytes(row.Down),
                    TotalText = FormatBytes(row.Up + row.Down),
                })
                .ToList());

            var today = daily.Count > 0 ? daily[^1] : new TrafficStatItem();
            var weekUp = daily.Sum(t => t.Up);
            var weekDown = daily.Sum(t => t.Down);

            TodayUpText = FormatBytes(today.Up);
            TodayDownText = FormatBytes(today.Down);
            WeekUpText = FormatBytes(weekUp);
            WeekDownText = FormatBytes(weekDown);
            TotalUpText = FormatBytes(weekUp);
            TotalDownText = FormatBytes(weekDown);

            var peakRow = hourly.OrderByDescending(t => t.Up + t.Down).FirstOrDefault();
            PeakHourText = peak > 0 && peakRow is not null
                ? $"峰值时段 {new DateTime(peakRow.HourTick):HH:00} · {FormatBytes(peak)}"
                : "还没有流量记录";
        }
        catch (Exception ex)
        {
            Logging.SaveLog("TrafficViewModel.Load", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string WeekdayZh(DateTime dt) => dt.DayOfWeek switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        _ => "日",
    };

    /// <summary>字节转可读文本。与首页速率的写法保持一致（1024 进制、一位小数）。</summary>
    public static string FormatBytes(long bytes)
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

        return unit == 0 ? $"{value:F0} {units[unit]}" : $"{value:F1} {units[unit]}";
    }
}

/// <summary>柱状图里的一小时。</summary>
public class TrafficHourItem
{
    /// <summary>小时标签，如 "08"。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>是否是当前这一小时（高亮用）。</summary>
    public bool IsCurrent { get; set; }

    public string UpText { get; set; } = string.Empty;

    public string DownText { get; set; } = string.Empty;

    /// <summary>上行柱高（px）。</summary>
    public double UpHeight { get; set; }

    /// <summary>下行柱高（px）。</summary>
    public double DownHeight { get; set; }

    /// <summary>整柱高度，用于给空桶留一个可见的底座。</summary>
    public double TotalHeight { get; set; }
}

/// <summary>按天汇总的一行。</summary>
public class TrafficDayItem
{
    public string Label { get; set; } = string.Empty;

    public string UpText { get; set; } = string.Empty;

    public string DownText { get; set; } = string.Empty;

    public string TotalText { get; set; } = string.Empty;
}
