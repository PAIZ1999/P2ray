using System.Windows;
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
            this.OneWayBind(ViewModel, vm => vm.Nodes, v => v.txtNodeCount.Text,
                    nodes => $"{nodes?.Count ?? 0} 条").DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.InboundPortDisplay, v => v.txtLocalPort.Text).DisposeWith(disposables);

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
