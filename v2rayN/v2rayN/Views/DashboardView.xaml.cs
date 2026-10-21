using System.Windows;
using System.Windows.Controls;
using ReactiveUI;
using ReactiveUI.Primitives.Signals;
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

            // 网络位置与连通性
            this.OneWayBind(ViewModel, vm => vm.NetProbes, v => v.lstNetProbes.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.NetProbeGroups, v => v.lstNetProbeGroups.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IpAddressDisplay, v => v.txtIpAddress.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IpLocationDisplay, v => v.txtIpLocation.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IpFlagUri, v => v.imgIpFlag.Source,
                    uri => ToImageSource(uri)).DisposeWith(disposables);

            // 国内出口：走代理时才有第二条，直连时它与上面是同一条数据
            this.OneWayBind(ViewModel, vm => vm.HasDomesticExit, v => v.domesticExitPanel.Visibility,
                    has => has ? Visibility.Visible : Visibility.Collapsed).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomesticIpAddressDisplay, v => v.txtDomesticIpAddress.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomesticIpLocationDisplay, v => v.txtDomesticIpLocation.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DomesticIpFlagUri, v => v.imgDomesticFlag.Source,
                    uri => ToImageSource(uri)).DisposeWith(disposables);

            // 标签随路径变化：走代理时上面那条确实是"境外出口"，
            // 直连时它就是本机地址，再叫"境外"会误导。
            this.OneWayBind(ViewModel, vm => vm.HasDomesticExit, v => v.txtExitLabel.Text,
                    has => has ? "境外出口 IP" : "当前出口 IP").DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.NetProbeStatus, v => v.txtNetProbeStatus.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.NetProbeSummary, v => v.txtNetProbeSummary.Text).DisposeWith(disposables);

            // 订阅更新状态：有内容才占位，避免空白行把卡片撑高
            this.OneWayBind(ViewModel, vm => vm.SubscriptionStatus, v => v.txtSubStatus.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SubscriptionStatus, v => v.txtSubStatus.Visibility,
                    s => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsNetProbeRunning, v => v.btnRefreshNetProbe.IsEnabled,
                    running => !running).DisposeWith(disposables);

            // 探测进行中只显示未分组的占位列表，出结果后换成按组显示。
            // 两者互斥，否则同一批站点会在界面上出现两次。
            this.OneWayBind(ViewModel, vm => vm.IsNetProbeRunning, v => v.lstNetProbes.Visibility,
                    running => running ? Visibility.Visible : Visibility.Collapsed).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsNetProbeRunning, v => v.lstNetProbeGroups.Visibility,
                    running => running ? Visibility.Collapsed : Visibility.Visible).DisposeWith(disposables);

            // IP 评分
            this.OneWayBind(ViewModel, vm => vm.HasIpScore, v => v.scorePanel.Visibility,
                    has => has ? Visibility.Visible : Visibility.Collapsed).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IpScoreDisplay, v => v.txtIpScore.Text).DisposeWith(disposables);
            // 分数染色在代码里做，不用 XAML Binding + AncestorType：
            // ReactiveUserControl 是泛型基类，XAML 里写不出来（实测报
            // "无法从文本 reactiveui:ReactiveUserControl 创建 AncestorType"）。
            this.OneWayBind(ViewModel, vm => vm.IpScoreValue, v => v.txtIpScore.Foreground,
                    score => Converters.ScoreBrushConverter.Instance.Convert(
                        score, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentCulture)
                        as System.Windows.Media.Brush).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IpScoreLevel, v => v.txtIpScoreLevel.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IpMetaDisplay, v => v.txtIpMeta.Text).DisposeWith(disposables);

            // 首屏探测：等内核就绪后再跑。
            //
            // 不能只延时 600ms 就测 —— 实测那时内核还没起来，探测走上直连路径，
            // 面板显示真实公网 IP 并标上"（直连）"，而几秒后代理其实已经工作了，
            // 结果整个面板的数据都是误导。
            //
            // 这里轮询等待本地 socks 端口可连接（最多 8 秒），再触发探测。
            // 未连接状态下 8 秒后照常测，此时直连数据本身就是正确答案。
            WaitForCoreThenProbe(d => d.DisposeWith(disposables));

            // 上游选中项变化时高亮对应按钮（含启动时的初始态）。
            this.WhenAnyValue(x => x.ViewModel!.SelectedRoutingMode)
                .Where(v => v is not null)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(v => SyncRoutingSelection(v))
                .DisposeWith(disposables);

            // 分段按钮的容器是延迟生成的：RoutingModes 刚填充时 ItemsControl 还没
            // 实例化出 RadioButton，此时同步会找不到任何按钮，界面表现为"一个都没选中"。
            // 因此集合变化后要再等一拍（布局跑完）重新同步一次。
            this.WhenAnyValue(x => x.ViewModel!.RoutingModes)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => ScheduleRoutingSelectionSync(d => d.DisposeWith(disposables)))
                .DisposeWith(disposables);

            // 路由模式说明文案由 ViewModel 统一提供：
            // 切换来源有两处（用户点选、启动时兜底选中），文案只该有一份。
            this.OneWayBind(ViewModel, vm => vm.RoutingHintText, v => v.txtRoutingHint.Text).DisposeWith(disposables);

            // 自定义直连：列表内容与操作结果提示
            this.OneWayBind(ViewModel, vm => vm.DirectRules, v => v.lstDirectRules.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DirectRuleStatus, v => v.txtDirectRuleStatus.Text,
                    s => string.IsNullOrEmpty(s) ? string.Empty : s).DisposeWith(disposables);

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

            // 连接状态一变就重测：出口 IP 和站点连通性在连接前后完全不同，
            // 不重测的话面板会一直挂着上一状态的数字，看起来像"连上了但出口还是本地"。
            this.WhenAnyValue(x => x.ViewModel!.IsConnected)
                .Skip(1)          // 跳过启动时的初值，那一次由 WaitForCoreThenProbe 负责
                .Throttle(TimeSpan.FromSeconds(2), RxSchedulers.MainThreadScheduler)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => ViewModel?.RefreshNetProbeCmd.Execute().Subscribe())
                .DisposeWith(disposables);
        });
    }

    private System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)FindResource(key);

    /// <summary>
    /// 等到本地代理端口可连接（或超时）后跑一次探测。
    ///
    /// 为什么需要它：首页和内核是并行启动的。内核就绪前探测会走直连，
    /// 测出来的 IP 与连通性都不代表"经过代理之后"的真实情况，
    /// 而用户看到的却是连接已建立 —— 数据与状态自相矛盾。
    /// </summary>
    /// <summary>
    /// 参数用 Action 而不接收 disposables：
    /// 本项目里 DisposeWith 的签名是 (IDisposable, Action)，而 disposables 变量的
    /// 具体类型来自隐式 using、写不出全名，所以由调用方把"注册清理"这一步传进来。
    /// </summary>
    private void WaitForCoreThenProbe(Action<IDisposable> disposeWith)
    {
        var port = AppManager.Instance.GetLocalPort(ServiceLib.Enums.EInboundProtocol.socks);

        // 每 250ms 试一次，最多 8 秒（32 次）。
        // 8 秒足够覆盖内核冷启动 + 首次配置生成；再久就按未连接处理。
        const int maxTries = 32;
        var tries = 0;
        var fired = false;

        var sub = Signal.Every(TimeSpan.FromMilliseconds(250))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (fired)
                {
                    return;
                }

                tries++;
                if (IsPortListening(port) || tries >= maxTries)
                {
                    fired = true;
                    ViewModel?.RefreshNetProbeCmd.Execute().Subscribe();
                }
            });

        disposeWith(sub);
    }

    /// <summary>本地端口是否有人在监听。50ms 超时对回环连接足够。</summary>
    private static bool IsPortListening(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            return client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(50))
                   && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// pack URI 转 ImageSource。
    ///
    /// 资源不存在时（比如国家不在素材集里）BitmapFrame.Create 会抛，
    /// 这里吞掉并返回 null，让图片位置留空，而不是让整个首页崩掉。
    /// </summary>
    private static System.Windows.Media.ImageSource? ToImageSource(string? uri)
    {
        if (uri.IsNullOrEmpty())
        {
            return null;
        }

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

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

    }

    /// <summary>
    /// 分段按钮的容器是延迟生成的，立即同步会找不到按钮（ItemsControl 尚未实例化模板）。
    /// 这里延两拍：先等布局，再等绑定。80ms 实测足够。
    /// </summary>
    private void ScheduleRoutingSelectionSync(Action<IDisposable> disposeWith)
    {
        var sub = Signal.Every(TimeSpan.FromMilliseconds(80))
            .Take(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => SyncRoutingSelection(ViewModel?.SelectedRoutingMode));

        disposeWith(sub);
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

        // 只写 ViewModel；说明文案由 RoutingHintText 绑定自动跟着变，
        // 视图不再自己维护一份映射。
        ViewModel!.SelectedRoutingMode = item;
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

    private void BtnRefreshNetProbe_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RefreshNetProbeCmd.Execute().Subscribe();
    }

    private void BtnToggleDirectRule_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.OpenDirectRulePanelCmd.Execute().Subscribe();
        directRulePanel.Visibility = ViewModel.IsDirectRulePanelOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        btnToggleDirectRule.Content = ViewModel.IsDirectRulePanelOpen ? "收起" : "配置";
    }

    private void BtnAddDirectRule_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        // 从控件读文本，不走 Binding：见 XAML 里的注释。
        ViewModel.DirectRuleInput = txtDirectRuleInput.Text;
        ViewModel.AddDirectRuleCmd.Execute().Subscribe();

        // 成功后 ViewModel 会清空 DirectRuleInput，这里同步回控件。
        txtDirectRuleInput.Text = ViewModel.DirectRuleInput ?? string.Empty;
    }

    private void BtnRemoveDirectRule_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: string ruleId } || ruleId.IsNullOrEmpty())
        {
            return;
        }

        // 删除是不可逆的（规则直接写库），先确认再动手。
        // 用应用内的 ConfirmDialog 而不是系统 MessageBox —— 后者是白底，在深色界面上很突兀。
        async void Remove()
        {
            var ok = await ConfirmDialog.ShowAsync("移除这条直连规则？", "移除", "取消");
            if (ok)
            {
                await ViewModel.RemoveDirectRuleAsync(ruleId);
            }
        }

        Remove();
    }
}
