using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReactiveUI;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>流量统计页：区间切换 + 折线趋势 + 每日明细。</summary>
public partial class TrafficView : ReactiveUserControl<TrafficViewModel>
{
    /// <summary>防止 RadioButton 的 Checked 与 VM 赋值互相触发。</summary>
    private bool _suppressRangeSync;

    public TrafficView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Ranges, v => v.lstRanges.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Days, v => v.lstDays.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Domains, v => v.lstDomains.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomainTotalText, v => v.txtDomainTotal.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomainHint, v => v.txtDomainHint.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomainHint, v => v.txtDomainHint.Visibility,
                    s => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Apps, v => v.lstApps.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.AppTotalText, v => v.txtAppTotal.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.AppHint, v => v.txtAppHint.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.AppHint, v => v.txtAppHint.Visibility,
                    s => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);
            // 子标签切换：三个面板互斥。可见性同时依赖 IsDomainTab 与 IsAppTab，
            // 用 OneWayBind 只会在其中一个变化时重算，必须合并订阅。
            this.WhenAnyValue(x => x.ViewModel!.IsDomainTab, x => x.ViewModel!.IsAppTab)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (ViewModel is null)
                    {
                        return;
                    }

                    var app = ViewModel.IsAppTab;
                    var domain = ViewModel.IsDomainTab;
                    summaryPanel.Visibility = !app && !domain ? Visibility.Visible : Visibility.Collapsed;
                    domainPanel.Visibility = domain && !app ? Visibility.Visible : Visibility.Collapsed;
                    appPanel.Visibility = app ? Visibility.Visible : Visibility.Collapsed;
                })
                .DisposeWith(disposables);
            // Y 轴四档刻度：写进四个固定 TextBlock，与网格线共用 0/56/113/170 的 y 值
            this.OneWayBind(ViewModel, vm => vm.YAxisLabels, v => v.txtY0.Text,
                    labels => labels is { Count: > 0 } ? labels[0] : string.Empty).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.YAxisLabels, v => v.txtY1.Text,
                    labels => labels is { Count: > 1 } ? labels[1] : string.Empty).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.YAxisLabels, v => v.txtY2.Text,
                    labels => labels is { Count: > 2 } ? labels[2] : string.Empty).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.YAxisLabels, v => v.txtY3.Text,
                    labels => labels is { Count: > 3 } ? labels[3] : string.Empty).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.XAxisLabels, v => v.lstXAxis.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DownPoints, v => v.lstHotspots.ItemsSource).DisposeWith(disposables);
            // 圆点与热区共用同一批点：坐标一致，悬停位置才对得上
            this.OneWayBind(ViewModel, vm => vm.DownPoints, v => v.lstDots.ItemsSource).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.RangeTotalDownText, v => v.txtRangeDown.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RangeTotalUpText, v => v.txtRangeUp.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RangeAverageDownText, v => v.txtAverageDown.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RangePeakDayText, v => v.txtPeakDay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RangePeakDownText, v => v.txtPeakDown.Text).DisposeWith(disposables);

            // 折线用 Geometry.Parse 转换：Path.Data 期待 Geometry，
            // 直接绑字符串会走类型转换器，字符串里出现非几何语法时整页会炸，
            // 这里显式解析并兜住异常。
            this.OneWayBind(ViewModel, vm => vm.DownLinePath, v => v.pathDownLine.Data,
                    ToGeometry).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.UpLinePath, v => v.pathUpLine.Data,
                    ToGeometry).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DownAreaPath, v => v.pathDownArea.Data,
                    ToGeometry).DisposeWith(disposables);

            // 选中态同步：容器是延迟生成的，布局跑完再设一次
            this.WhenAnyValue(x => x.ViewModel!.SelectedRange)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => Dispatcher.BeginInvoke(new Action(SyncRangeSelection)))
                .DisposeWith(disposables);
        });
        // 容器是延迟生成的：Loaded 时可能还没生成，用 Background 优先级再补一次
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(SyncRangeSelection),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static Geometry? ToGeometry(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Geometry.Parse(path);
        }
        catch
        {
            return null;
        }
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
    /// Click 一定会在鼠标点击时触发，作为兜底。
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
            // 点击后强制校正一次选中态，避免组内互斥把高亮留在别的项上
            Dispatcher.BeginInvoke(new Action(SyncRangeSelection), System.Windows.Threading.DispatcherPriority.Background);
        }
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

    /// <summary>子标签切换。按域名标签会触发一次数据刷新。</summary>
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        ViewModel.IsDomainTab = tag == "domain";
        ViewModel.IsAppTab = tag == "app";
        ViewModel.RefreshCmd.Execute().Subscribe();
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
