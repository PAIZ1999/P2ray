using ServiceLib.Resx;
using System.Globalization;
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

    /// <summary>上次选择的区间天数，进程内记忆。</summary>
    private static int LastSelectedDays = 7;

    /// <summary>可选的时间区间（天）。</summary>
    public IReadOnlyList<TrafficRange> Ranges { get; } =
    [
        new(ResUIHelper.Get("P2TrafficRange3"), 3),
        new(ResUIHelper.Get("P2TrafficRange7"), 7),
        new(ResUIHelper.Get("P2TrafficRange30"), 30),
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

    /// <summary>折线数据点，供视图直接绘制与命中测试。</summary>
    [Reactive]
    public partial BulkObservableCollection<TrafficSeriesPoint> Series { get; set; } = [];

    /// <summary>Y 轴上限（含顶部留白后的值），视图据此换算像素。</summary>
    [Reactive]
    public partial double SeriesMax { get; set; }

    /// <summary>Y 轴上限的可读文本，如 "30.9 MB"。</summary>
    [Reactive]
    public partial string SeriesMaxText { get; set; } = string.Empty;

    /// <summary>当前是否处于「按域名」子标签。</summary>
    [Reactive]
    public partial bool IsDomainTab { get; set; }

    /// <summary>当前内核是否支持按域名统计。</summary>
    [Reactive]
    public partial bool IsDomainSupported { get; set; }

    /// <summary>不支持时给用户的说明。</summary>
    [Reactive]
    public partial string DomainHint { get; set; } = string.Empty;

    /// <summary>按域名聚合的结果。</summary>
    [Reactive]
    public partial BulkObservableCollection<DomainStatRow> Domains { get; set; } = [];

    /// <summary>按域名统计的合计下行，用于摘要文案。</summary>
    [Reactive]
    public partial string DomainTotalText { get; set; } = "—";

    /// <summary>当前是否处于「按应用」子标签。</summary>
    [Reactive]
    public partial bool IsAppTab { get; set; }

    /// <summary>按应用统计是否可用（需管理员）。</summary>
    [Reactive]
    public partial bool IsAppSupported { get; set; }

    [Reactive]
    public partial string AppHint { get; set; } = string.Empty;

    [Reactive]
    public partial BulkObservableCollection<DomainStatRow> Apps { get; set; } = [];

    /// <summary>合计标题：随口径变化，避免"仅代理"时还写着"全机"。</summary>
    [Reactive]
    public partial string AppTotalLabel { get; set; } = "区间内按应用下行合计（仅代理）";

    /// <summary>按应用口径说明文案。</summary>
    [Reactive]
    public partial string AppScopeHint { get; set; } = string.Empty;

    [Reactive]
    public partial string AppTotalText { get; set; } = "—";

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

        // 记住上次选的区间：反复进出页面时不必每次重新点。
        // 进程内静态即可满足，跨进程持久化需要新增配置项，暂不做。
        SelectedRange = Ranges.FirstOrDefault(r => r.Days == LastSelectedDays) ?? Ranges[1];

        // 切区间即重算，不做数据缓存：一次查询最多 30 行，成本可以忽略。
        this.WhenAnyValue(x => x.SelectedRange)
            .Subscribe(range =>
            {
                if (range is not null)
                {
                    LastSelectedDays = range.Days;
                }

                RefreshCmd.Execute().Subscribe();
            });

        _ = LoadAsync();
    }

    /// <summary>
    /// 生成当前标签页、当前区间的 CSV。返回文件名与内容。
    ///
    /// 导出「看到的这一屏」而不是三张表全倒出来：用户在域名页点导出，
    /// 期望拿到的就是域名榜，不需要再到 Excel 里筛。
    /// </summary>
    public async Task<(string Name, string Csv)> BuildExportAsync()
    {
        var days = SelectedRange?.Days ?? 7;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");

        if (IsDomainTab)
        {
            var list = await DomainStatService.Instance.GetRangeAsync(days);
            return ($"p2ray-domain-{stamp}.csv",
                TrafficCsvExporter.Build(list.Select(t => new TrafficCsvExporter.Row(t.Key, t.Up, t.Down)), "domain"));
        }

        if (IsAppTab)
        {
            var list = await AppStatService.Instance.GetRangeAsync(days);
            return ($"p2ray-process-{stamp}.csv",
                TrafficCsvExporter.Build(list.Select(t => new TrafficCsvExporter.Row(t.Key, t.Up, t.Down)), "process"));
        }

        var daily = await TrafficStatService.Instance.GetDailyAsync(days);
        return ($"p2ray-daily-{stamp}.csv",
            TrafficCsvExporter.Build(
                daily.Select(r => new TrafficCsvExporter.Row(new DateTime(r.HourTick).ToString("yyyy-MM-dd"), r.Up, r.Down)),
                "date"));
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
            var scaleMax = maxValue > 0 ? maxValue * 1.08 : 1;

            BuildChart(rows, scaleMax, days);
            BuildDetails(rows);
            BuildSummary(rows, days);
            await BuildDomainsAsync(days);
            await BuildAppsAsync(days);
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

        // 交给视图绘制：VM 只提供数值与文案，像素换算在视图里做，
        // 这样窗口缩放时不用重算 VM，也不会出现坐标与控件尺寸不一致。
        Series = new BulkObservableCollection<TrafficSeriesPoint>(rows.Select(r => new TrafficSeriesPoint
        {
            Date = r.Date,
            Up = r.Up,
            Down = r.Down,
            DateText = r.Date.ToString("MM/dd"),
            UpText = FormatBytes(r.Up),
            DownText = FormatBytes(r.Down),
        }));
        SeriesMax = scaleMax;
        SeriesMaxText = FormatBytes((long)scaleMax);

        DownPoints = new BulkObservableCollection<TrafficPoint>(points);
        DownLinePath = string.Join(" ", downSegments);
        UpLinePath = string.Join(" ", upSegments);
        DownAreaPath = string.Join(" ", areaSegments);

        // Y 轴：4 档刻度，最高档即 scaleMax
        // 没有数据时只有底部一个 0：四档都算成 0 B 会显示成 "0 B / 0 B / 0 B / 0"，
// 看起来像渲染错误。
        YAxisLabels = scaleMax > 1
            ? new BulkObservableCollection<string>(
            [
                FormatBytes((long)scaleMax),
                FormatBytes((long)(scaleMax * 0.66)),
                FormatBytes((long)(scaleMax * 0.33)),
                "0",
            ])
            : new BulkObservableCollection<string>(["", "", "", "0"]);

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
                Label = row.Date.ToString("MM-dd ") + row.Date.ToString("ddd", CultureInfo.CurrentUICulture),
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
        // 区间内一点流量都没有时，显示"峰值日 10-17 / 0 B"会让人以为那天真有数据
        if (totalDown == 0 && totalUp == 0)
        {
            RangePeakDayText = "—";
            RangePeakDownText = string.Empty;
        }
        else
        {
            RangePeakDayText = peak.Date == default ? "—" : peak.Date.ToString("MM-dd");
            RangePeakDownText = FormatBytes(peak.Down);
        }
    }

    /// <summary>
    /// 按域名聚合。数据来自 clash API，仅 mihomo / sing-box 内核下有内容；
    /// 其它内核如实显示原因，而不是给一个空列表让人以为坏了。
    /// </summary>
    private async Task BuildDomainsAsync(int days)
    {
        IsDomainSupported = DomainStatService.IsSupported;
        DomainHint = DomainStatService.UnsupportedHint;

        if (!IsDomainSupported)
        {
            Domains = [];
            DomainTotalText = "—";
            return;
        }

        var list = await DomainStatService.Instance.GetRangeAsync(days);
        var total = list.Sum(t => t.Down);
        DomainTotalText = FormatBytes(total);

        var max = list.Count > 0 ? list.Max(t => t.Down + t.Up) : 0;
        var rows = list.Take(50).Select(t =>
        {
            var sum = t.Down + t.Up;
            return new DomainStatRow
            {
                Host = t.Key,
                DownText = FormatBytes(t.Down),
                UpText = FormatBytes(t.Up),
                ShareText = max > 0 ? $"{sum * 100.0 / max:F0}%" : "0%",
                DownBarWidth = Math.Max(320.0 * t.Down / (max > 0 ? max : 1), t.Down > 0 ? 2 : 0),
                UpBarWidth = Math.Max(320.0 * t.Up / (max > 0 ? max : 1), t.Up > 0 ? 2 : 0),
            };
        }).ToList();

        Domains = new BulkObservableCollection<DomainStatRow>(rows);
    }

    /// <summary>
    /// <summary>
    /// 按应用聚合。数据与「按域名」同源（clash API），口径也相同：仅代理，不含直连。
    /// 差别只在于归因维度 —— 域名用目标 host，应用用 sourcePort → 进程。
    /// </summary>
    /// </summary>
    private async Task BuildAppsAsync(int days)
    {

        AppTotalLabel = "区间内按应用下行合计（仅代理）";
        AppScopeHint = "与「按域名」同源：统计经代理的流量，按连接来源端口归因到具体程序。直连流量不计入。";
        IsAppSupported = AppStatService.IsSupported;
        AppHint = AppStatService.Instance.StatusHint;

        if (!IsAppSupported)
        {
            Apps = [];
            AppTotalText = "—";
            return;
        }

        var list = await AppStatService.Instance.GetRangeAsync(days);
        AppTotalText = FormatBytes(list.Sum(t => t.Down));

        var max = list.Count > 0 ? list.Max(t => t.Down + t.Up) : 0;
        Apps = new BulkObservableCollection<DomainStatRow>(list.Take(50).Select(t =>
        {
            var sum = t.Down + t.Up;
            return new DomainStatRow
            {
                Host = t.Key,
                DownText = FormatBytes(t.Down),
                UpText = FormatBytes(t.Up),
                ShareText = max > 0 ? $"{sum * 100.0 / max:F0}%" : "0%",
                DownBarWidth = Math.Max(320.0 * t.Down / (max > 0 ? max : 1), t.Down > 0 ? 2 : 0),
                UpBarWidth = Math.Max(320.0 * t.Up / (max > 0 ? max : 1), t.Up > 0 ? 2 : 0),
            };
        }).ToList());
    }

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

/// <summary>按域名统计的一行。</summary>
public class DomainStatRow
{
    public string Host { get; set; } = string.Empty;

    public string DownText { get; set; } = string.Empty;

    public string UpText { get; set; } = string.Empty;

    public string ShareText { get; set; } = string.Empty;

    public double DownBarWidth { get; set; }

    public double UpBarWidth { get; set; }
}
/// <summary>折线上的一个数据点。像素坐标由视图计算，VM 只给数值与文案。</summary>
public class TrafficSeriesPoint
{
    public DateTime Date { get; set; }

    public long Up { get; set; }

    public long Down { get; set; }

    public string DateText { get; set; } = string.Empty;

    public string UpText { get; set; } = string.Empty;

    public string DownText { get; set; } = string.Empty;
}
