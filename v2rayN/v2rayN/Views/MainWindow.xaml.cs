using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using v2rayN.Base;
using v2rayN.Manager;

namespace v2rayN.Views;

public partial class MainWindow
{
    private static Config _config;
    private readonly SingleReplaceableDisposable _layoutBindingsDisposable = new();
    private CheckUpdateView? _checkUpdateView;
    private DashboardViewModel? _dashboardViewModel;
    private ContentControl? _hiddenProfilesHost;
    private ContentControl? _hiddenStatusBarHost;
    private BackupAndRestoreView? _backupAndRestoreView;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        ThreadPool.RegisterWaitForSingleObject(App.ProgramStarted, OnProgramStarted, null, -1, false);

        App.Current.SessionEnding += Current_SessionEnding;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        this.WhenActivated(disposables =>
        {
            _layoutBindingsDisposable.DisposeWith(disposables);

            InitNavigation();

            // 底部状态栏已并入导航栏，状态显示由 NavRailView 承担，
            // 但 StatusBarView 仍需挂载：它的 WhenActivated 注册了托盘与统计相关的 Interaction。
            EnsureStatusBarInteractionsRegistered();

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(interaction =>
            {
                var clipboardData = WindowsUtils.GetClipboardData();
                interaction.SetOutput(clipboardData);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(false);
                if (Application.Current?.MainWindow is { } window)
                {
                    var bytes = QRCodeWindowsUtils.CaptureScreen(window);
                    interaction.SetOutput(bytes);
                }
                ShowHideWindow(true);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(interaction =>
            {
                if (UI.OpenFileDialog(out var fileName, "PNG|*.png|All|*.*") != true)
                {
                    interaction.SetOutput(null);
                    return;
                }
                interaction.SetOutput(fileName);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
             .AsObservable()
             .ObserveOn(RxSchedulers.MainThreadScheduler)
             .Subscribe(Shutdown)
             .DisposeWith(disposables);
        });

        Title = $"{Utils.GetVersion()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";
        if (_config.UiItem.AutoHideStartup)
        {
            WindowState = WindowState.Minimized;
        }

        if (!_config.GuiItem.EnableHWA)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }

        WindowsManager.Instance.RegisterGlobalHotkey(_config, OnHotkeyHandler, null);
    }

    #region Event

    private void OnProgramStarted(object state, bool timeout)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ShowHideWindow(true);
        });
    }

    private async Task DelegateSnackMsg(string content)
    {
        MainSnackbar.MessageQueue?.Enqueue(content);
        await Task.CompletedTask;
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                ShowHideWindow(null);
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        ShowHideWindow(false);
    }

    private async void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Logging.SaveLog("Current_SessionEnding");
        StorageUI();
        await AppManager.Instance.AppExitAsync(false);
    }

    private void Shutdown(bool obj)
    {
        Application.Current.Shutdown();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            switch (e.Key)
            {
                case Key.V:
                    if (Keyboard.FocusedElement is TextBox)
                    {
                        return;
                    }
                    AddServerViaClipboardAsync().ContinueWith(_ => { });

                    break;

                case Key.S:
                    ScanScreenTaskAsync().ContinueWith(_ => { });
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        StorageUI();
        ShowHideWindow(false);
    }

    private void MenuSettingsSetUWP_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = WindowsUtils.GetClipboardData();
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    private async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        if (Application.Current?.MainWindow is Window window)
        {
            var bytes = QRCodeWindowsUtils.CaptureScreen(window);
            await ViewModel?.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        ShowCheckUpdateDialog();
    }

    private void MenuBackupAndRestore_Click(object sender, RoutedEventArgs e)
    {
        ShowBackupRestoreDialog();
    }

    /// <summary>
    /// 打开检查更新面板。
    /// CheckUpdateView / BackupAndRestoreView 是 UserControl 而非 Window，
    /// WindowDialog.ShowDialogAsync 只能解析 Window，对它们会静默返回 false，
    /// 因此这两个面板必须走主窗口的 DialogHost。
    /// </summary>
    public void ShowCheckUpdateDialog()
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        DialogHost.Show(_checkUpdateView, "RootDialog");

        AppEvents.HasUpdateNotified.Publish(false);
    }

    /// <summary>打开备份与恢复面板。原因同上。</summary>
    public void ShowBackupRestoreDialog()
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView, "RootDialog");
    }

    /// <summary>
    /// 供页面内按钮调用：找到主窗口并弹出检查更新。
    /// </summary>
    public static void RequestCheckUpdate()
    {
        (Application.Current?.MainWindow as MainWindow)?.ShowCheckUpdateDialog();
    }

    public static void RequestBackupRestore()
    {
        (Application.Current?.MainWindow as MainWindow)?.ShowBackupRestoreDialog();
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ?? !AppManager.Instance.ShowInTaskbar;
        if (bl)
        {
            this?.Show();
            if (this?.WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            this?.Activate();
            this?.Focus();
        }
        else
        {
            this?.Hide();
        }
        AppManager.Instance.ShowInTaskbar = bl;
    }

    /// <summary>
    /// 在窗口句柄创建后、窗口显示前立刻设置深色标题栏。
    ///
    /// 不能放在 OnLoaded：那时窗口已经以浅色边框绘制过至少一帧，
    /// 用户会看到白色标题栏"闪一下再变黑"。
    /// 也不能放到构造函数：DwmSetWindowAttribute 需要有效 HWND。
    /// OnSourceInitialized 是唯一可靠的时机。
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 不能传 CurrentTheme：P2ray 界面配色是硬编码深色，而 CurrentTheme 默认
        // FollowSystem，在浅色系统上会算出 isDark=false，白标题栏配深色界面必然割裂。
        WindowsUtils.SetDarkBorder(this, nameof(ETheme.Dark));
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);

        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
        RestoreUI();
    }

    private void RestoreUI()
    {
        // 旧版的三套可切换布局已移除，此处不再需要恢复分栏比例。
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);
    }
    /// <summary>
    /// 三页切换：首页 / 节点 / 设置。同一时刻只显示一页。
    /// </summary>
    private void ShowPage(NavPage page)
    {
        // 顺序关键：先置可见再创建 View，确保 WhenActivated 能触发。
        contentHome.Visibility = page == NavPage.Home ? Visibility.Visible : Visibility.Collapsed;
        contentNodes.Visibility = page == NavPage.Nodes ? Visibility.Visible : Visibility.Collapsed;
        contentTraffic.Visibility = page == NavPage.Traffic ? Visibility.Visible : Visibility.Collapsed;
        contentSettings.Visibility = page == NavPage.Settings ? Visibility.Visible : Visibility.Collapsed;

        EnsurePageLoaded(page);
    }

    private enum NavPage
    {
        Home,
        Nodes,
        Traffic,
        Settings
    }

    /// <summary>
    /// 惰性挂载：View 必须在其容器可见时创建，否则 ReactiveUI 的 WhenActivated 不触发，
    /// View 内注册的 Interaction 处理器缺失，VM 调用时抛 UnhandledInteractionException。
    /// </summary>
    private void EnsurePageLoaded(NavPage page)
    {
        // 三个页面共用同一个 DashboardViewModel，不能走 ViewHost（其按 VM 类型查视图）。
        switch (page)
        {
            case NavPage.Home when contentHome.Content == null:
                var home = new DashboardView { ViewModel = _dashboardViewModel };
                contentHome.Content = home;
                home.AttachLogView(ViewModel.MsgViewModel);
                break;
            case NavPage.Nodes when contentNodes.Content == null:
                contentNodes.Content = new NodesView { ViewModel = _dashboardViewModel };
                break;
            case NavPage.Traffic when contentTraffic.Content == null:
                // 流量页有自己的 VM：它只是读库展示，和首页的实时数据无关
                contentTraffic.Content = new TrafficView { ViewModel = new TrafficViewModel() };
                break;
            // 每次切回流量页都重新载入：用户很可能刚连过网，数据变了
            case NavPage.Traffic:
                (contentTraffic.Content as TrafficView)?.ViewModel?.RefreshCmd.Execute().Subscribe();
                break;
            case NavPage.Settings when contentSettings.Content == null:
                contentSettings.Content = new SettingsView { ViewModel = _dashboardViewModel };
                break;
        }
    }

    /// <summary>
    /// 初始化导航栏与三页内容。
    /// </summary>
    private void InitNavigation()
    {
        _dashboardViewModel = new DashboardViewModel(ViewModel.StatusBarViewModel, ViewModel.ProfilesViewModel);

        var navRail = new NavRailView();
        navRail.ViewModel = _dashboardViewModel;
        navRailHost.Content = navRail;

        // 原顶栏的 40+ 个命令绑定已迁入侧栏「更多」菜单
        navRail.InitializeCommands(ViewModel);

        navRail.navHome.Checked += (_, _) => ShowPage(NavPage.Home);
        navRail.navNodes.Checked += (_, _) => ShowPage(NavPage.Nodes);
        navRail.navTraffic.Checked += (_, _) => ShowPage(NavPage.Traffic);
        navRail.navSettings.Checked += (_, _) => ShowPage(NavPage.Settings);

        // 首页「去挑选」按钮触发的跨页导航
        _dashboardViewModel.NavigateToNodesRequested += () =>
        {
            Dispatcher.Invoke(() => navRail.navNodes.IsChecked = true);
        };

        // ProfilesViewModel 在启动阶段就会调用 DispatcherRefreshServersBizInteraction，
        // 其处理器由 ProfilesView 的 WhenActivated 注册。ProfilesView 已不在主界面中，
        // 改为在后台挂载一次以确保处理器注册完成。
        EnsureProfilesInteractionsRegistered();

        navRail.navHome.IsChecked = true;
        ShowPage(NavPage.Home);




    }

    /// <summary>
    /// StatusBarView 已不在主界面展示，但其 WhenActivated 注册了托盘与统计相关的 Interaction，
    /// 这里用同一个隐藏宿主挂载一次。
    /// </summary>
    private void EnsureStatusBarInteractionsRegistered()
    {
        if (_hiddenStatusBarHost != null)
        {
            return;
        }

        _hiddenStatusBarHost = new ContentControl
        {
            Visibility = Visibility.Visible,
            Width = 0,
            Height = 0,
            IsHitTestVisible = false
        };

        HiddenHost.Children.Add(_hiddenStatusBarHost);
        ViewHost.Show(_hiddenStatusBarHost, ViewModel.StatusBarViewModel);
    }
    /// <summary>
    /// ProfilesView 不再出现在主界面，但 ProfilesViewModel 仍依赖它注册的 Interaction。
    /// 这里用一个隐藏宿主挂载一次，保证处理器存在。
    /// </summary>
    private void EnsureProfilesInteractionsRegistered()
    {
        if (_hiddenProfilesHost != null)
        {
            return;
        }

        _hiddenProfilesHost = new ContentControl
        {
            Visibility = Visibility.Visible,
            Width = 0,
            Height = 0,
            IsHitTestVisible = false
        };

        // 必须真实加入可视树，WhenActivated 才会触发
        HiddenHost.Children.Add(_hiddenProfilesHost);
        ViewHost.Show(_hiddenProfilesHost, ViewModel.ProfilesViewModel);
    }



    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag.ToString());
        }
    }

    #endregion UI
}