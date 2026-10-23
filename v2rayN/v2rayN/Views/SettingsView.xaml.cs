using System.Windows;
using ReactiveUI;
using ServiceLib.Manager;

namespace v2rayN.Views;

/// <summary>
/// 设置页：macOS 分组卡片风格。
/// 开关类设置直接写回 Config 并落盘，不引入"保存"按钮 —— 改完即生效符合系统设置的预期。
/// 需要重启内核才生效的项（局域网、UDP、日志级别）在写回后给出明确提示。
/// </summary>
public partial class SettingsView : ReactiveUserControl<DashboardViewModel>
{
    /// <summary>自动更新的可选间隔（小时），0 表示关闭。</summary>
    private static readonly int[] _autoUpdateOptions = [0, 6, 12, 24, 48];

    private static readonly string[] _logLevelOptions = ["warning", "info", "debug", "error", "none"];

    public SettingsView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Subscriptions, v => v.txtSubCount.Text,
                    subs => subs is { Count: > 0 } ? $"已配置 {subs.Count} 个订阅" : "尚未配置订阅")
                .DisposeWith(disposables);
        });

        var config = AppManager.Instance.Config;

        togAutoRun.IsChecked = config.GuiItem.AutoRun;
        togAutoHideStartup.IsChecked = config.UiItem.AutoHideStartup;
        togAllowLan.IsChecked = config.Inbound.First().AllowLANConn;
        togUdp.IsChecked = config.Inbound.First().UdpEnabled;

        txtDataDir.Text = Utils.StartupPath();
        txtVersionAbout.Text = Utils.GetVersion(false);

        RefreshAutoUpdateButton();
        RefreshLogLevelButton();
        RefreshRetentionButton();
    }

    #region 通用

    private async void TogAutoRun_Click(object sender, RoutedEventArgs e)
    {
        var config = AppManager.Instance.Config;
        config.GuiItem.AutoRun = togAutoRun.IsChecked == true;

        // 自启动同时写注册表/计划任务，只改 Config 不会真的生效。
        await AutoStartupHandler.UpdateTask(config);
        await ConfigHandler.SaveConfig(config);

        NoticeManager.Instance.Enqueue(config.GuiItem.AutoRun ? "已设置开机自启动" : "已取消开机自启动");
    }

    private async void TogAutoHideStartup_Click(object sender, RoutedEventArgs e)
    {
        var config = AppManager.Instance.Config;
        config.UiItem.AutoHideStartup = togAutoHideStartup.IsChecked == true;
        await ConfigHandler.SaveConfig(config);

        NoticeManager.Instance.Enqueue("下次启动生效");
    }

    private async void BtnAutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        var config = AppManager.Instance.Config;
        var current = Array.IndexOf(_autoUpdateOptions, config.GuiItem.AutoUpdateInterval);
        config.GuiItem.AutoUpdateInterval = _autoUpdateOptions[(current + 1) % _autoUpdateOptions.Length];

        await ConfigHandler.SaveConfig(config);
        RefreshAutoUpdateButton();
    }

    private void RefreshAutoUpdateButton()
    {
        var hours = AppManager.Instance.Config.GuiItem.AutoUpdateInterval;
        btnAutoUpdate.Content = hours <= 0 ? "关闭" : $"{hours} 小时";
    }

    #endregion

    #region 网络

    private async void TogAllowLan_Click(object sender, RoutedEventArgs e)
    {
        var config = AppManager.Instance.Config;
        config.Inbound.First().AllowLANConn = togAllowLan.IsChecked == true;
        await ConfigHandler.SaveConfig(config);

        NoticeManager.Instance.Enqueue("重新连接后生效");
    }

    private async void TogUdp_Click(object sender, RoutedEventArgs e)
    {
        var config = AppManager.Instance.Config;
        config.Inbound.First().UdpEnabled = togUdp.IsChecked == true;
        await ConfigHandler.SaveConfig(config);

        NoticeManager.Instance.Enqueue("重新连接后生效");
    }

    private async void BtnLogLevel_Click(object sender, RoutedEventArgs e)
    {
        var config = AppManager.Instance.Config;
        var current = Array.IndexOf(_logLevelOptions, config.CoreBasicItem.Loglevel);
        config.CoreBasicItem.Loglevel = _logLevelOptions[(current + 1) % _logLevelOptions.Length];

        await ConfigHandler.SaveConfig(config);
        RefreshLogLevelButton();
        NoticeManager.Instance.Enqueue("重新连接后生效");
    }

    private void RefreshLogLevelButton()
    {
        btnLogLevel.Content = AppManager.Instance.Config.CoreBasicItem.Loglevel;
    }

    #endregion

    #region 订阅

    private async void BtnManageSub_Click(object sender, RoutedEventArgs e)
    {
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(new SubSettingViewModel()) == true)
        {
            await ViewModel.LoadSubscriptionsAsync();
        }
    }

    private void BtnUpdateSub_Click(object sender, RoutedEventArgs e)
    {
        // 复用上游的订阅更新流程（走代理，失败回退直连）
        ViewModel?.UpdateSubscriptionCmd.Execute().Subscribe();
        NoticeManager.Instance.Enqueue("正在更新订阅…");
    }

    #endregion

    #region 高级功能

    private async void BtnCoreSetting_Click(object sender, RoutedEventArgs e)
        => await ShowAsync<OptionSettingViewModel>();

    private async void BtnRouting_Click(object sender, RoutedEventArgs e)
        => await ShowAsync<RoutingSettingViewModel>();

    private async void BtnDns_Click(object sender, RoutedEventArgs e)
        => await ShowAsync<DNSSettingViewModel>();

    private async void BtnFullConfig_Click(object sender, RoutedEventArgs e)
        => await ShowAsync<FullConfigTemplateViewModel>();

    private async void BtnHotkey_Click(object sender, RoutedEventArgs e)
        => await ShowAsync<GlobalHotkeySettingViewModel>();

    /// <summary>备份与恢复是 UserControl 面板，只能走主窗口 DialogHost。</summary>
    private void BtnBackup_Click(object sender, RoutedEventArgs e)
        => MainWindow.RequestBackupRestore();

    private async void BtnRebootAdmin_Click(object sender, RoutedEventArgs e)
        => await AppManager.Instance.RebootAsAdmin();

    private async void BtnClearStat_Click(object sender, RoutedEventArgs e)
    {
        await StatisticsManager.Instance.ClearAllServerStatistics();
        NoticeManager.Instance.Enqueue("统计数据已清除");
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        // 同样是 UserControl 面板，走主窗口 DialogHost。
        MainWindow.RequestCheckUpdate();
        await Task.CompletedTask;
    }

    private static async Task ShowAsync<TViewModel>() where TViewModel : class, new()
    {
        await AppManager.Instance.WindowDialog.ShowDialogAsync(new TViewModel());
    }

    #endregion

    #region 数据

    /// <summary>保留期可选值：0 表示永久保留。</summary>
    private static readonly int[] _retentionOptions = [30, 90, 365, 0];

    private async void BtnRetention_Click(object sender, RoutedEventArgs e)
    {
        var config = AppManager.Instance.Config;
        var current = Array.IndexOf(_retentionOptions, config.GuiItem.TrafficRetentionDays);
        config.GuiItem.TrafficRetentionDays = _retentionOptions[(current + 1) % _retentionOptions.Length];

        await ConfigHandler.SaveConfig(config);
        RefreshRetentionButton();
    }

    private void RefreshRetentionButton()
    {
        var days = AppManager.Instance.Config.GuiItem.TrafficRetentionDays;
        btnRetention.Content = days <= 0 ? "永久保留" : $"{days} 天";
    }
    private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.StartupPath());
    }

    private void BtnOpenLogDir_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetLogPath());
    }

    #endregion
}
