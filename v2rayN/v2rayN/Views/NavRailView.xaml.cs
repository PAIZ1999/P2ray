using System.Windows;
using ReactiveUI;
using ReactiveUI.Primitives.Signals;
using ServiceLib.Manager;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>
/// 侧栏：品牌、三项导航、连接状态、两个常用快捷入口。
/// 高级功能已迁移到设置页的「高级功能」分组，不再使用二级菜单。
/// </summary>
public partial class NavRailView : ReactiveUserControl<DashboardViewModel>
{
    private MainWindowViewModel? _host;

    public NavRailView()
    {
        InitializeComponent();
        txtVersion.Text = Utils.GetVersion(false);

        btnImport.Click += async (_, _) => await AppManager.Instance.WindowDialog.ShowDialogAsync(new SubSettingViewModel());

        // 检查更新是 UserControl 面板，走主窗口 DialogHost，不能用 WindowDialog。
        btnCheckUpdateRail.Click += (_, _) => MainWindow.RequestCheckUpdate();

        this.WhenActivated(disposables =>
        {
            // 用 VM 的三态文案，而不是自己再判一次 IsConnected：
            // 首页已经会显示"连接异常"，侧栏若还说"已连接"，两处自相矛盾。
            this.OneWayBind(ViewModel, vm => vm.ConnectionStatusText, v => v.txtStatus.Text).DisposeWith(disposables);

            // 状态点同样三态。两条 OneWayBind 都写 Fill 会互相覆盖，改成一个订阅。
            this.WhenAnyValue(
                    x => x.ViewModel!.IsConnected,
                    x => x.ViewModel!.CorePortReady,
                    (connected, ready) => !connected
                        ? "P2rayIdleBrush"
                        : ready == false ? "P2rayWarningBrush" : "P2raySuccessBrush")
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(key => ellStatus.Fill = (System.Windows.Media.Brush)FindResource(key))
                .DisposeWith(disposables);
        });
    }

    /// <summary>
    /// 由 MainWindow 挂载后调用，接管命令绑定。
    /// </summary>
    public void InitializeCommands(MainWindowViewModel host)
    {
        _host = host;
    }

    private void BtnStatusCard_Click(object sender, RoutedEventArgs e)
    {
        navHome.IsChecked = true;
    }
}
