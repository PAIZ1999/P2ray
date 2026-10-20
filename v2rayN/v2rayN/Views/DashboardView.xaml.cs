using System.Windows;
using System.Windows.Controls;
using ReactiveUI;
using ServiceLib.Manager;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>
/// 首页：面向不懂网络的用户。
///
/// 设计取舍：
/// 1. 主控区不用卡片包裹 —— 卡片边框会把界面切成碎片，深色主题下更显沉闷。
///    改用大字号线路名建立视觉锚点，靠留白划分区域。
/// 2. 只保留一个主动作，不暴露协议名、端口、内核名等术语。
/// 3. 技术细节（日志）默认折叠。
/// </summary>
public partial class DashboardView : ReactiveUserControl<DashboardViewModel>
{
    private bool _logExpanded;

    public DashboardView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.RunningServerDisplay, v => v.txtCurrentServer.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.ConnectionHint, v => v.txtHint.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.UploadDisplay, v => v.txtUpload.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DownloadDisplay, v => v.txtDownload.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunTimeDisplay, v => v.txtRunTime.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.CoreTypeDisplay, v => v.txtCoreType.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Subscriptions, v => v.lstSubscriptions.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.NodeCount, v => v.txtNodeCount.Text,
                    count => $"{count} 条").DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.InboundPortDisplay, v => v.txtLocalPort.Text).DisposeWith(disposables);

            // 路由模式：分段按钮，数量与名称都来自上游 RoutingItems。
            this.OneWayBind(ViewModel, vm => vm.RoutingModes, v => v.lstRoutingModes.ItemsSource).DisposeWith(disposables);

            // 上游选中项变化时高亮对应按钮（含启动时的初始态）。
            this.WhenAnyValue(x => x.ViewModel!.SelectedRoutingMode)
                .Where(v => v is not null)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(v => SyncRoutingSelection(v))
                .DisposeWith(disposables);

            // 连接方式开关：状态源是 StatusBarViewModel，单向镜像到开关，点击时反向写回。
            var statusBar = StatusBarViewModel.Instance;
            statusBar.WhenAnyValue(x => x.BlSystemProxySet)
                .Subscribe(v => togSystemProxy.IsChecked = v)
                .DisposeWith(disposables);
            statusBar.WhenAnyValue(x => x.EnableTun)
                .Subscribe(v => togTun.IsChecked = v)
                .DisposeWith(disposables);

            // 连接状态：按钮互斥 + 文案 + 着色
            this.OneWayBind(ViewModel, vm => vm.IsConnected, v => v.btnConnect.Visibility,
                    x => x ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsConnected, v => v.btnDisconnect.Visibility,
                    x => x ? Visibility.Visible : Visibility.Collapsed).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsConnected, v => v.txtStatus.Text,
                    x => x ? "已连接" : "未连接").DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsConnected, v => v.txtInternet.Text,
                    x => x ? "可以正常上网" : "无法访问境外网站").DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsConnected, v => v.ellStatus.Fill,
                    x => Brush(x ? "P2raySuccessBrush" : "P2rayIdleBrush")).DisposeWith(disposables);

            // 订阅有无决定引导区与列表的可见性
            this.OneWayBind(ViewModel, vm => vm.Subscriptions, v => v.guidePanel.Visibility,
                    subs => subs is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Subscriptions, v => v.lstSubscriptions.Visibility,
                    subs => subs is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed).DisposeWith(disposables);
        });
    }

    private System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)FindResource(key);

    /// <summary>
    /// 把日志视图挂到首页。MsgViewModel 是共享单例，只能挂在一个宿主上。
    /// </summary>
    public void AttachLogView(MsgViewModel? msgViewModel)
    {
        msgHost.Content = msgViewModel is null
            ? null
            : new LogPanelView { ViewModel = msgViewModel };
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ConnectCmd.Execute().Subscribe();
    }

    private void TogSystemProxy_Click(object sender, RoutedEventArgs e)
    {
        var statusBar = StatusBarViewModel.Instance;
        var cmd = togSystemProxy.IsChecked == true ? statusBar.SystemProxySetCmd : statusBar.SystemProxyClearCmd;
        cmd.Execute().Subscribe();
    }

    private void TogTun_Click(object sender, RoutedEventArgs e)
    {
        StatusBarViewModel.Instance.EnableTun = togTun.IsChecked == true;
    }

    private void CmbRouting_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 提示文案随模式变化：从"规则"切到"全局"意味着流量走向完全变了，
        // 用户需要知道当前处于哪种模式，而不是只看到一段按钮里高亮了哪一个。
        if (ViewModel?.SelectedRoutingMode is not { } item)
        {
            return;
        }

        UpdateRoutingHint(item.Remarks);
    }

    /// <summary>
    /// 把 ViewModel 的当前路由模式同步到分段按钮的选中态。
    /// ItemsControl 生成的容器与数据项顺序一致，但容器可能是延迟生成的，
    /// 因此用 Tag 反查而不依赖索引位置。
    /// </summary>
    private void SyncRoutingSelection(ServiceLib.Models.Entities.RoutingItem? selected)
    {
        if (selected is null)
        {
            return;
        }

        foreach (var btn in EnumerateRouteButtons())
        {
            btn.IsChecked = ReferenceEquals(btn.Tag, selected)
                            || (btn.Tag is ServiceLib.Models.Entities.RoutingItem it && it.Id == selected.Id);
        }

        UpdateRoutingHint(selected.Remarks);
    }

    /// <summary>分段按钮都在 lstRoutingModes 里，逐项取容器再取内容。</summary>
    private IEnumerable<RadioButton> EnumerateRouteButtons()
    {
        if (lstRoutingModes.ItemsSource is null)
        {
            yield break;
        }

        // 模板已实例化，直接遍历可视树即可
        foreach (var btn in FindVisualChildren<RadioButton>(lstRoutingModes))
        {
            yield return btn;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
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

    private void RoutingMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: ServiceLib.Models.Entities.RoutingItem item })
        {
            return;
        }

        ViewModel!.SelectedRoutingMode = item;
        UpdateRoutingHint(item.Remarks);
    }

    /// <summary>把上游的 "V4-绕过大陆(Whitelist)" 翻译成一句人话。</summary>
    private void UpdateRoutingHint(string? remarks)
    {
        // 用和按钮标签同一套映射（规则 / 黑名单 / 全局），避免按钮写着"规则"、
        // 下面的说明却按"绕过大陆"分支去匹配，两边判断标准不一致。
        var name = Converters.RoutingNameConverter.ToShortName(remarks);
        txtRoutingHint.Text = name switch
        {
            "全局" => "全部网站都走代理。速度取决于线路，流量消耗也最大。",
            "黑名单" => "只有列表内的网站走代理，其余直连。适合明确知道要访问哪些站点的场景。",
            "规则" => "国内网站直连，境外网站走代理。日常使用推荐这一项。",
            _ => "决定哪些网站的流量经过代理。"
        };
    }

    private void BtnCopyPort_Click(object sender, RoutedEventArgs e)
    {
        var port = ViewModel?.InboundPortDisplay;
        if (port.IsNullOrEmpty() || port == "—")
        {
            return;
        }

        // 多端口时上游会给 "10808 / 10809"，复制时只取主端口。
        var first = port.Split('/')[0].Trim();
        WindowsUtils.SetClipboardData(first);
    }

    private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.DisconnectCmd.Execute().Subscribe();
    }

    private void BtnUpdateSub_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.UpdateSubscriptionCmd.Execute().Subscribe();
    }

    private async void BtnAddSub_Click(object sender, RoutedEventArgs e)
    {
        var vm = new SubSettingViewModel();
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(vm) == true)
        {
            await ViewModel.LoadSubscriptionsAsync();
        }
    }

    private void BtnGoNodes_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RequestNavigateToNodes();
    }

    private void BtnToggleLog_Click(object sender, RoutedEventArgs e)
    {
        _logExpanded = !_logExpanded;
        logCard.Visibility = _logExpanded ? Visibility.Visible : Visibility.Collapsed;
        btnToggleLog.Content = _logExpanded ? "隐藏运行日志" : "显示运行日志";
    }
}
