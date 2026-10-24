using System.Windows.Threading;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using ReactiveUI;
using ReactiveUI.Primitives.Signals;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>
/// 流量统计页：区间切换 + 折线趋势 + 每日明细。
///
/// 折线图是自绘的（Canvas + Path），不用第三方图表库：
/// 本项目只需要"一条面积线 + 十字准线 + 悬停卡片"，为此引入一整套图表依赖
/// 收益不大；自己画反而能把网格、留白、命中测试都调到与界面其余部分一致。
/// </summary>
public partial class TrafficView : ReactiveUserControl<TrafficViewModel>
{
    private const double PlotHeight = 200;
    private const double YGutter = 62;

    /// <summary>防止 RadioButton 的 Checked 与 VM 赋值互相触发。</summary>
    private bool _suppressRangeSync;

    // 绘图元素只创建一次，重绘时改坐标，避免频繁重建可视树
    private readonly Path _areaPath = new() { StrokeThickness = 0 };
    private readonly Path _downPath = new()
    {
        StrokeThickness = 2.4,
        StrokeLineJoin = PenLineJoin.Round,
    };
    private readonly Path _upPath = new()
    {
        StrokeThickness = 1.4,
        StrokeDashArray = [3, 3],
        Opacity = 0.9,
    };
    /// <summary>每个数据点的小圆点，重绘时重建。</summary>
    private readonly List<Ellipse> _dots = [];

    private readonly Line _crossLine = new() { StrokeThickness = 1, Visibility = Visibility.Collapsed };
    private readonly Ellipse _hoverDot = new() { Width = 7, Height = 7, Visibility = Visibility.Collapsed };
    private readonly Ellipse _hoverDotUp = new() { Width = 6, Height = 6, Visibility = Visibility.Collapsed };

    private bool _layerReady;
    private int _hoverIndex = -1;

    public TrafficView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Ranges, v => v.lstRanges.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Days, v => v.lstDays.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Domains, v => v.lstDomains.ItemsSource).DisposeWith(disposables);
            // 列头随内核切换口径：clash 系是「下行/上行」，Xray 下是「连接次数」
            this.OneWayBind(ViewModel, vm => vm.DomainDownHeader, v => v.txtDomainDownHeader.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomainUpHeader, v => v.txtDomainUpHeader.Text).DisposeWith(disposables);
            // Xray 口径下没有「上行」列：宽度给 0 即收起，不必引入转换器
            this.OneWayBind(ViewModel, vm => vm.DomainUpWidth, v => v.txtDomainUpHeader.Width).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomainHintTop, v => v.txtDomainHintTop.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomainTotalLabel, v => v.txtDomainTotalLabel.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Apps, v => v.lstApps.ItemsSource).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.RangeTotalDownText, v => v.txtRangeDown.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RangeTotalUpText, v => v.txtRangeUp.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RangeAverageDownText, v => v.txtAverageDown.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RangePeakDayText, v => v.txtPeakDay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RangePeakDownText, v => v.txtPeakDown.Text).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.DomainTotalText, v => v.txtDomainTotal.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomainHint, v => v.txtDomainHint.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomainHint, v => v.txtDomainHint.Visibility,
                    s => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.AppTotalText, v => v.txtAppTotal.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.AppScopeHint, v => v.txtAppScope.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.AppTotalLabel, v => v.txtAppTotalLabel.Text).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.AppHint, v => v.txtAppHint.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.AppHint, v => v.txtAppHint.Visibility,
                    s => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.HasData, v => v.txtChartEmpty.Visibility,
                    has => has ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);

            // 子标签切换：三个面板互斥。可见性同时依赖两个状态，必须合并订阅。
            this.WhenAnyValue(x => x.ViewModel!.IsDomainTab, x => x.ViewModel!.IsAppTab)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => ApplyTabVisibility())
                .DisposeWith(disposables);

            // 数据变化 → 重绘
            this.WhenAnyValue(x => x.ViewModel!.Series, x => x.ViewModel!.SeriesMax)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => RenderChart())
                .DisposeWith(disposables);

            // 容器延迟生成：布局跑完再同步区间高亮
            this.WhenAnyValue(x => x.ViewModel!.Days)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => Dispatcher.BeginInvoke(new Action(SyncRangeSelection)))
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.ViewModel!.SelectedRange)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => Dispatcher.BeginInvoke(new Action(SyncRangeSelection)))
                .DisposeWith(disposables);
        });

        // 实时刷新：统计每 2 秒轮询、5 秒落盘；页面每 3 秒取一次数。
        // 原来只在进入页面时读一次库，用户下载时看到的是一动不动的数字。
        RealtimeTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background,
            (_, _) => RefreshIfVisible(), Dispatcher);
        RealtimeTimer.Start();

        Loaded += (_, _) =>
        {
            EnsureLayer();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncRangeSelection();
                RenderChart();
            }), System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    /// <summary>防止标签选中态与 VM 互相触发。</summary>
    private bool _suppressTabSync;

    /// <summary>把 VM 的子标签状态同步回三个 RadioButton。</summary>
    private void SyncTabSelection()
    {
        if (ViewModel is null)
        {
            return;
        }

        _suppressTabSync = true;
        try
        {
            tabSummary.IsChecked = !ViewModel.IsDomainTab && !ViewModel.IsAppTab;
            tabDomain.IsChecked = ViewModel.IsDomainTab;
            tabApp.IsChecked = ViewModel.IsAppTab;
        }
        finally
        {
            _suppressTabSync = false;
        }
    }

    private readonly DispatcherTimer? RealtimeTimer;
    private bool _refreshing;

    /// <summary>只在页面可见时刷新；切到其它页或正在刷新时跳过。</summary>
    private void RefreshIfVisible()
    {
        if (ViewModel is null || !IsVisible || _refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            ViewModel.RefreshCmd.Execute().Subscribe(_ => _refreshing = false, _ => _refreshing = false);
        }
        catch
        {
            _refreshing = false;
        }
    }

    private void ApplyTabVisibility()
    {
        if (ViewModel is null)
        {
            return;
        }

        SyncTabSelection();

        var app = ViewModel.IsAppTab;
        var domain = ViewModel.IsDomainTab;
        summaryPanel.Visibility = !app && !domain ? Visibility.Visible : Visibility.Collapsed;
        domainPanel.Visibility = domain && !app ? Visibility.Visible : Visibility.Collapsed;
        appPanel.Visibility = app ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EnsureLayer()
    {
        if (_layerReady)
        {
            return;
        }

        _areaPath.SetResourceReference(Shape.FillProperty, "P2rayAccentSoftBrush");
        _downPath.SetResourceReference(Shape.StrokeProperty, "P2rayAccentBrush");
        _upPath.SetResourceReference(Shape.StrokeProperty, "P2raySuccessBrush");
        _crossLine.SetResourceReference(Shape.StrokeProperty, "P2rayBorderBrush");
        _hoverDot.SetResourceReference(Shape.FillProperty, "P2rayAccentBrush");
        _hoverDotUp.SetResourceReference(Shape.FillProperty, "P2raySuccessBrush");

        plotLayer.Children.Add(_areaPath);
        plotLayer.Children.Add(_downPath);
        plotLayer.Children.Add(_upPath);
        plotLayer.Children.Add(_crossLine);
        plotLayer.Children.Add(_hoverDot);
        plotLayer.Children.Add(_hoverDotUp);
        _layerReady = true;
    }

    private void Plot_SizeChanged(object sender, SizeChangedEventArgs e) => RenderChart();

    /// <summary>重绘网格、刻度、面积与折线。窗口尺寸或数据变化时调用。</summary>
    private void RenderChart()
    {
        EnsureLayer();

        var points = ViewModel?.Series;
        var width = plotHost.ActualWidth;
        if (points is null || points.Count == 0 || width <= 1)
        {
            _areaPath.Data = null;
            _downPath.Data = null;
            _upPath.Data = null;
            yAxisLayer.Children.Clear();
            xAxisLayer.Children.Clear();
            return;
        }

        var max = ViewModel?.SeriesMax ?? 1;
        if (max <= 0)
        {
            max = 1;
        }

        var right = width;
        var bottom = PlotHeight;

        // ── 网格线 + Y 轴刻度（4 档，压线居中） ──
        yAxisLayer.Children.Clear();
        for (var i = 0; i <= 3; i++)
        {
            var ratio = i / 3.0;
            var y = bottom - bottom * ratio;

            var line = new Line
            {
                X1 = 0,
                X2 = right,
                Y1 = y,
                Y2 = y,
                StrokeThickness = 1,
                Opacity = i == 0 ? 0.55 : 0.28,
            };
            line.SetResourceReference(Shape.StrokeProperty, "P2rayDividerBrush");
            plotLayer.Children.Insert(i, line);

            // 没有数据时只保留底部那个 0：四档都画会显示成 "1 B / 0 B / 0 B / 0"，
            // 看起来像渲染错误
            var label = new TextBlock
            {
                Text = max <= 1 && i != 0 ? string.Empty : FormatAxis(max * ratio),
                FontSize = 10,
                Width = YGutter - 10,
                TextAlignment = TextAlignment.Right,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "P2rayTextTertiaryBrush");
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 7);
            yAxisLayer.Children.Add(label);
        }

        // 网格线插入后，折线元素需要保持在最上层
        Panel.SetZIndex(_areaPath, 2);
        Panel.SetZIndex(_downPath, 3);
        Panel.SetZIndex(_upPath, 4);
        Panel.SetZIndex(_crossLine, 5);
        Panel.SetZIndex(_hoverDot, 6);
        Panel.SetZIndex(_hoverDotUp, 7);

        // ── 折线与面积 ──
        var stepX = points.Count > 1 ? right / (points.Count - 1) : 0;
        var downGeo = new StreamGeometry();
        var upGeo = new StreamGeometry();
        using (var ctx = downGeo.Open())
        {
            for (var i = 0; i < points.Count; i++)
            {
                var x = points.Count > 1 ? i * stepX : right / 2;
                var y = bottom - bottom * Math.Clamp(points[i].Down / max, 0, 1);
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        using (var ctx = upGeo.Open())
        {
            for (var i = 0; i < points.Count; i++)
            {
                var x = points.Count > 1 ? i * stepX : right / 2;
                var y = bottom - bottom * Math.Clamp(points[i].Up / max, 0, 1);
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        downGeo.Freeze();
        upGeo.Freeze();
        _downPath.Data = downGeo;
        _upPath.Data = upGeo;

        // 面积：折线首尾闭合到底边
        if (points.Count > 1)
        {
            var areaGeo = new StreamGeometry();
            using (var ctx = areaGeo.Open())
            {
                ctx.BeginFigure(new Point(0, bottom), true, true);
                for (var i = 0; i < points.Count; i++)
                {
                    var x = i * stepX;
                    var y = bottom - bottom * Math.Clamp(points[i].Down / max, 0, 1);
                    ctx.LineTo(new Point(x, y), true, false);
                }

                ctx.LineTo(new Point(right, bottom), true, false);
            }

            areaGeo.Freeze();
            _areaPath.Data = areaGeo;
        }
        else
        {
            _areaPath.Data = null;
        }

        // ── 数据点圆点：≤14 个点时才画，30 天会糊成一条带 ──
        foreach (var dot in _dots)
        {
            plotLayer.Children.Remove(dot);
        }

        _dots.Clear();
        if (points.Count <= 14)
        {
            for (var i = 0; i < points.Count; i++)
            {
                var x = points.Count > 1 ? i * stepX : right / 2;
                var y = bottom - bottom * Math.Clamp(points[i].Down / max, 0, 1);
                var dot = new Ellipse { Width = 5, Height = 5, Opacity = 0.95 };
                dot.SetResourceReference(Shape.FillProperty, "P2rayAccentBrush");
                Canvas.SetLeft(dot, x - 2.5);
                Canvas.SetTop(dot, y - 2.5);
                Panel.SetZIndex(dot, 5);
                plotLayer.Children.Add(dot);
                _dots.Add(dot);
            }
        }

        // ── X 轴刻度：最多 7 个，均匀取样，避免重叠 ──
        xAxisLayer.Children.Clear();
        var labelStep = Math.Max(1, (int)Math.Ceiling(points.Count / 7.0));
        for (var i = 0; i < points.Count; i += labelStep)
        {
            var x = points.Count > 1 ? i * stepX : right / 2;
            var label = new TextBlock
            {
                Text = points[i].DateText,
                FontSize = 10,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "P2rayTextTertiaryBrush");
            Canvas.SetLeft(label, Math.Clamp(x - 18, 0, Math.Max(0, right - 40)));
            Canvas.SetTop(label, 0);
            xAxisLayer.Children.Add(label);
        }

        HideHover();
    }

    /// <summary>Y 轴刻度文案：短一些，避免长单位把绘图区挤窄。</summary>
    private static string FormatAxis(double value)
    {
        if (value <= 0)
        {
            return "0";
        }

        string[] units = ["B", "K", "M", "G", "T"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{(int)value} B" : $"{value:F0} {units[unit]}";
    }

    private void Plot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var points = ViewModel?.Series;
        if (points is null || points.Count == 0)
        {
            return;
        }

        var width = plotHost.ActualWidth;
        if (width <= 1)
        {
            return;
        }

        var pos = e.GetPosition(plotHost);
        var stepX = points.Count > 1 ? width / (points.Count - 1) : 0;
        var index = stepX > 0 ? (int)Math.Round(pos.X / stepX) : 0;
        ShowHoverAt(index, pos.X);
    }

    /// <summary>把十字准线、圆点与数值卡片移动到第 index 个点。posX 为鼠标在绘图区内的 x。</summary>
    private void ShowHoverAt(int index, double posX)
    {
        var points = ViewModel?.Series;
        if (points is null || points.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, points.Count - 1);
        _hoverIndex = index;

        var width = plotHost.ActualWidth;
        if (width <= 1)
        {
            return;
        }

        var max = ViewModel?.SeriesMax ?? 1;
        if (max <= 0)
        {
            max = 1;
        }

        var stepX = points.Count > 1 ? width / (points.Count - 1) : 0;
        var x = points.Count > 1 ? index * stepX : width / 2;
        var yDown = PlotHeight - PlotHeight * Math.Clamp(points[index].Down / max, 0, 1);
        var yUp = PlotHeight - PlotHeight * Math.Clamp(points[index].Up / max, 0, 1);

        _crossLine.X1 = x;
        _crossLine.X2 = x;
        _crossLine.Y1 = 0;
        _crossLine.Y2 = PlotHeight;
        _crossLine.Visibility = Visibility.Visible;

        _hoverDot.Visibility = Visibility.Visible;
        Canvas.SetLeft(_hoverDot, x - 3.5);
        Canvas.SetTop(_hoverDot, yDown - 3.5);

        _hoverDotUp.Visibility = Visibility.Visible;
        Canvas.SetLeft(_hoverDotUp, x - 3);
        Canvas.SetTop(_hoverDotUp, yUp - 3);

        txtHoverDate.Text = points[index].Date.ToString("yyyy-MM-dd ddd");
        txtHoverDown.Text = points[index].DownText;
        txtHoverUp.Text = points[index].UpText;

        hoverCard.Visibility = Visibility.Visible;
        var cardWidth = 132.0;
        var left = Math.Clamp(posX + 14, 0, Math.Max(0, width - cardWidth));
        hoverCard.Margin = new Thickness(YGutter + left, 8, 0, 0);
    }

    private void Plot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => HideHover();

    private void HideHover()
    {
        _hoverIndex = -1;
        _crossLine.Visibility = Visibility.Collapsed;
        _hoverDot.Visibility = Visibility.Collapsed;
        _hoverDotUp.Visibility = Visibility.Collapsed;
        hoverCard.Visibility = Visibility.Collapsed;
    }

    private void SyncRangeSelection()
    {
        if (ViewModel is null)
        {
            return;
        }

        _suppressRangeSync = true;
        try
        {
            foreach (var rb in FindVisualChildren<RadioButton>(lstRanges))
            {
                rb.IsChecked = rb.Tag is TrafficRange range
                               && ViewModel.SelectedRange is not null
                               && range.Days == ViewModel.SelectedRange.Days;
            }
        }
        finally
        {
            _suppressRangeSync = false;
        }
    }

    private void Range_Checked(object sender, RoutedEventArgs e) => ApplyRange(sender);

    /// <summary>
    /// 再挂一个 Click：RadioButton 在自定义模板下，若初始 IsChecked 已被写成
    /// 其它项，单靠 Checked 可能不触发（同组互斥会吞掉点击）。
    /// </summary>
    private void Range_Click(object sender, RoutedEventArgs e) => ApplyRange(sender);

    private void ApplyRange(object sender)
    {
        if (_suppressRangeSync || ViewModel is null)
        {
            return;
        }

        if (sender is RadioButton { Tag: TrafficRange range })
        {
            ViewModel.SelectedRange = range;
            Dispatcher.BeginInvoke(new Action(SyncRangeSelection),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>切换「按应用」的统计口径，立即重新取数。</summary>


    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressTabSync || ViewModel is null || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        ViewModel.IsDomainTab = tag == "domain";
        ViewModel.IsAppTab = tag == "app";
        ViewModel.RefreshCmd.Execute().Subscribe();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>把当前标签页、当前区间的数据导出成 CSV（带 BOM，Excel 直接能开）。</summary>
    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var (name, csv) = await ViewModel.BuildExportAsync();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = name,
            Filter = "CSV|*.csv",
            DefaultExt = ".csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // UTF-8 带 BOM：不带的话 Excel 打开中文/特殊字符会乱码
            await File.WriteAllTextAsync(dialog.FileName, csv, new UTF8Encoding(true));
            MessageBox.Show($"已导出到\n{dialog.FileName}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var ret = MessageBox.Show("将删除全部流量历史记录，该操作不可撤销。继续？",
            "清空统计", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ret != MessageBoxResult.OK)
        {
            return;
        }

        ViewModel.ClearCmd.Execute().Subscribe();
    }
}
