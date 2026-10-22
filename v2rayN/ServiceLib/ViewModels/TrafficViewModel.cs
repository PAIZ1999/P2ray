namespace ServiceLib.ViewModels;

/// <summary>
/// 流量统计页。
///
/// 数据口径：只统计经 P2ray 转发的代理流量（含规则下走直连的部分不算），
/// 因为核心只对它自己处理过的连接上报计数。
/// </summary>
public partial class TrafficViewModel : ReactiveObject
{
    /// <summary>折线图绘图区高度（px）。</summary>
    private const double ChartHeight = 170;

    /// <summary>折线图绘图区宽度（px）。逻辑宽度固定，实际按容器拉伸。</summary>
    private const double ChartWidth = 1000;

    /// <summary>可选的时间区间（天）。</summary>
    public IReadOnlyList<TrafficRange> Ranges { get; } =
    [
        new("3 天", 3),
        new("7 天", 7),
        new("30 天", 30),
    ];

    [Reactive]
    public partial TrafficRange? SelectedRange { get; set; }

    /// <summary>折线点（按天）。</summary>
    [Reactive]
    public partial BulkObservableCollection<TrafficPoint> DownPoints { get; set; } = [];

    [Reactive]
    public partial BulkObservableCollection<TrafficPoint> UpPoints { get; set; } = [];

    /// <summary>折线段的 Path 数据。</summary>
    [Reactive]
    public partial string DownLinePath { get; set; } = string.Empty;

    [Reactive]
    public partial string UpLinePath { get; set; } = string.Empty;

    [Reactive]
    public partial string DownAreaPath { get; set; } = string.Empty;

    /// <summary>Y 轴刻度（从高到低）。</summary>
    [Reactive]
    public partial BulkObservableCollection<string> YAxisLabels { get; set; } = [];

    /// <summary>X 轴刻度标签。</summary>
    [Reactive]
    public partial BulkObservableCollection<TrafficAxisLabel> XAxisLabels { get; set; } = [];

    [Reactive]
    public partial string RangeTotalDownText { get; set; } = "—";

    [Reactive]
    public partial string RangeTotalUpText { get; set; } = "—";

    [Reactive]
    public partial string RangeAverageDownText { get; set; } = "—";

    [Reactive]
    public partial string RangePeakDayText { get; set; } = "—";

    [Reactive]
    public partial string RangePeakDownText { get; set; } = "—";

    /// <summary>明细行（按天，含占比）。</summary>
    [Reactive]
    public partial BulkObservableCollection<TrafficDayItem> Days { get; set; } = [];

    [Reactive]
    public partial bool HasData { get; set; }

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

        SelectedRange = Ranges[1];

        // 切区间即重算，不做数据缓存：一次查询最多 30 行，成本可以忽略。
        this.WhenAnyValue(x => x.SelectedRange)

            .Subscribe(_ => RefreshCmd.Execute().Subscribe());

        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            var days = SelectedRange?.Days ?? 7;
            var daily = await TrafficStatService.Instance.GetDailyAsync(days);

            // 用元组而不是匿名类型：调用方签名是具名元组，匿名类型无法隐式转换
            var rows = daily
                .Select(r => (Date: new DateTime(r.HourTick), r.Up, r.Down))
                .ToList();

            var maxValue = rows.Count > 0 ? rows.Max(r => Math.Max(r.Down, r.Up)) : 0;
            HasData = maxValue > 0;

            // 顶部固定留 15% 空白，峰值不会顶到边框
            var scaleMax = maxValue > 0 ? maxValue * 1.15 : 1;

            BuildChart(rows, scaleMax, days);
            BuildDetails(rows);
            BuildSummary(rows, days);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("TrafficViewModel.Load", ex);
        }
    }

    /// <summary>
    /// 生成折线。用一条 Path 而不是逐点画圆：30 个点时圆点会糊成一片，
    /// 折线本身更清楚；悬停信息由透明热区负责。
    /// </summary>
    private void BuildChart(List<(DateTime Date, long Up, long Down)> rows, double scaleMax, int days)
    {
        var points = new List<TrafficPoint>();
        var downSegments = new List<string>();
        var upSegments = new List<string>();
        var areaSegments = new List<string>();

        var count = rows.Count;
        if (count == 0)
        {
            DownPoints = [];
            DownLinePath = UpLinePath = DownAreaPath = string.Empty;
            return;
        }

        // 单点时避免除零
        var stepX = count > 1 ? ChartWidth / (count - 1) : 0;

        for (var i = 0; i < count; i++)
        {
            var row = rows[i];
            var x = count > 1 ? i * stepX : ChartWidth / 2;
            var yDown = ChartHeight - ChartHeight * (row.Down / scaleMax);
            var yUp = ChartHeight - ChartHeight * (row.Up / scaleMax);

            points.Add(new TrafficPoint
            {
                X = x,
                YDown = yDown,
                YUp = yUp,
                CenterX = x,
                // 热区：以该点为中心、宽度为一个步长，覆盖整个柱形区间
                HotX = count > 1 ? Math.Max(0, x - stepX / 2) : 0,
                HotWidth = count > 1 ? stepX : ChartWidth,
                Tooltip = $"{row.Date:MM-dd ddd}\n下行 {FormatBytes(row.Down)}\n上行 {FormatBytes(row.Up)}",
                ShowLabel = count <= 10 || i % Math.Max(1, count / 7) == 0,
                Label = $"{row.Date:M/d}",
                DotX = x - 3,
                DotY = yDown - 3,
                IsDotVisible = count <= 10,
            });

            downSegments.Add($"{(i == 0 ? "M" : "L")} {x:F1},{yDown:F1}");
            upSegments.Add($"{(i == 0 ? "M" : "L")} {x:F1},{yUp:F1}");
            areaSegments.Add($"{(i == 0 ? "M" : "L")} {x:F1},{yDown:F1}");
        }

        // 面积填充：折线 → 底边闭合
        if (count > 1)
        {
            areaSegments.Add($"L {ChartWidth:F1},{ChartHeight:F1}");
            areaSegments.Add($"L 0,{ChartHeight:F1}");
            areaSegments.Add("Z");
        }

        DownPoints = new BulkObservableCollection<TrafficPoint>(points);
        DownLinePath = string.Join(" ", downSegments);
        UpLinePath = string.Join(" ", upSegments);
        DownAreaPath = string.Join(" ", areaSegments);

        // Y 轴：4 档刻度，最高档即 scaleMax
        YAxisLabels = new BulkObservableCollection<string>(
        [
            FormatBytes((long)scaleMax),
            FormatBytes((long)(scaleMax * 0.66)),
            FormatBytes((long)(scaleMax * 0.33)),
            "0",
        ]);

        XAxisLabels = new BulkObservableCollection<TrafficAxisLabel>(
            points.Where(p => p.ShowLabel)
                .Select(p => new TrafficAxisLabel { X = p.X - 14, Text = p.Label })
                .ToList());
    }

    private void BuildDetails(List<(DateTime Date, long Up, long Down)> rows)
    {
        var maxTotal = rows.Count > 0 ? rows.Max(r => r.Down + r.Up) : 0;
        var items = new List<TrafficDayItem>();

        // 明细按时间倒序：最近的一天在最上面
        foreach (var row in rows.OrderByDescending(r => r.Date))
        {
            var total = row.Down + row.Up;
            items.Add(new TrafficDayItem
            {
                Label = row.Date.ToString("MM-dd ") + WeekdayZh(row.Date),
                UpText = FormatBytes(row.Up),
                DownText = FormatBytes(row.Down),
                TotalText = FormatBytes(total),
                ShareText = maxTotal > 0 ? $"{total * 100.0 / maxTotal:F0}%" : "0%",
                DownRatio = maxTotal > 0 ? (double)row.Down / maxTotal : 0,
                UpRatio = maxTotal > 0 ? (double)row.Up / maxTotal : 0,
                DownBarWidth = Math.Max((maxTotal > 0 ? (double)row.Down / maxTotal : 0) * 320, row.Down > 0 ? 2 : 0),
                UpBarWidth = Math.Max((maxTotal > 0 ? (double)row.Up / maxTotal : 0) * 320, row.Up > 0 ? 2 : 0),
            });
        }

        Days = new BulkObservableCollection<TrafficDayItem>(items);
    }

    private void BuildSummary(List<(DateTime Date, long Up, long Down)> rows, int days)
    {
        var totalDown = rows.Sum(r => r.Down);
        var totalUp = rows.Sum(r => r.Up);

        RangeTotalDownText = FormatBytes(totalDown);
        RangeTotalUpText = FormatBytes(totalUp);
        RangeAverageDownText = FormatBytes(rows.Count > 0 ? totalDown / rows.Count : 0);

        var peak = rows.OrderByDescending(r => r.Down).FirstOrDefault();
        RangePeakDayText = peak.Date == default ? "—" : peak.Date.ToString("MM-dd");
        RangePeakDownText = FormatBytes(peak.Down);
    }

    private static string WeekdayZh(DateTime dt) => dt.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日",
    };

    /// <summary>字节转可读文本，1024 进制、最多一位小数。</summary>
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

/// <summary>时间区间选项。</summary>
public class TrafficRange(string label, int days)
{
    public string Label { get; } = label;

    public int Days { get; } = days;

    public override string ToString() => Label;
}

/// <summary>折线上的一个点，同时充当悬停热区。</summary>
public class TrafficPoint
{
    public double X { get; set; }

    /// <summary>下行折线的 Y 坐标（画布坐标系，向下为正）。</summary>
    public double YDown { get; set; }

    /// <summary>上行折线的 Y 坐标。</summary>
    public double YUp { get; set; }

    /// <summary>圆点画在折线上，需要中心坐标。</summary>
    public double CenterX { get; set; }

    public double HotX { get; set; }

    public double HotWidth { get; set; }

    /// <summary>圆点左上角坐标（中心坐标减去半径）。</summary>
    public double DotX { get; set; }

    /// <summary>圆点距顶距离。</summary>
    public double DotY { get; set; }

    /// <summary>点数过多时隐藏圆点，避免糊成一片。
    /// ServiceLib 不引用 WPF，所以这里只给布尔值，由视图转成 Visibility。</summary>
    public bool IsDotVisible { get; set; } = true;

    /// <summary>悬停提示文本。</summary>
    public string Tooltip { get; set; } = string.Empty;

    /// <summary>X 轴标签文本。</summary>
    public string Label { get; set; } = string.Empty;

    public bool ShowLabel { get; set; }
}

/// <summary>X 轴刻度。用百分比定位，容器变宽时自动跟随。</summary>
public class TrafficAxisLabel
{
    /// <summary>在绘图区坐标系里的 X 位置（与折线同一坐标系，随 Viewbox 缩放）。</summary>
    public double X { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>明细表里的一行。</summary>
public class TrafficDayItem
{
    public string Label { get; set; } = string.Empty;

    public string UpText { get; set; } = string.Empty;

    public string DownText { get; set; } = string.Empty;

    public string TotalText { get; set; } = string.Empty;

    /// <summary>占区间内最大单日总量的比例文案，如 "46%"。</summary>
    public string ShareText { get; set; } = string.Empty;

    public double DownRatio { get; set; }

    public double UpRatio { get; set; }

    /// <summary>下行条宽度（px），按比例换算到固定满格宽度。</summary>
    public double DownBarWidth { get; set; }

    /// <summary>上行条宽度（px）。</summary>
    public double UpBarWidth { get; set; }
}
