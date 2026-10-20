using System.Windows;
using ReactiveUI;
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
            this.OneWayBind(ViewModel, vm => vm.IsConnected, v => v.txtStatus.Text,
                connected => connected ? "已连接" : "未连接").DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsConnected, v => v.ellStatus.Fill,
                connected => connected
                    ? (System.Windows.Media.Brush)FindResource("P2raySuccessBrush")
                    : (System.Windows.Media.Brush)FindResource("P2rayIdleBrush")).DisposeWith(disposables);
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
