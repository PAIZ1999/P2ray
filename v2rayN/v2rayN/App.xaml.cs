using v2rayN.Manager;
using v2rayN.Views;

namespace v2rayN;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    public static EventWaitHandle ProgramStarted;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    /// <summary>
    /// Open only one process
    /// </summary>
    /// <param name="e"></param>
    protected override void OnStartup(StartupEventArgs e)
    {
        var exePathKey = Utils.GetMd5(Utils.GetExePath());

        var rebootas = e.Args.Any(t => t == Global.RebootAs);
        ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset, exePathKey, out var bCreatedNew);
        if (!rebootas && !bCreatedNew)
        {
            ProgramStarted.Set();
            Environment.Exit(0);
            return;
        }

        if (!AppManager.Instance.InitApp())
        {
            UI.Show($"Loading GUI configuration file is abnormal,please restart the application{Environment.NewLine}加载GUI配置文件异常,请重启应用");
            Environment.Exit(0);
            return;
        }

        AppManager.Instance.WindowDialog = new WindowDialog();

        AppManager.Instance.InitComponents();

        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();

        base.OnStartup(e);

        var mainWindowViewModel = new MainWindowViewModel();
        var viewFor = SimpleViewLocator.Instance.ResolveView(mainWindowViewModel);
        viewFor!.ViewModel = mainWindowViewModel;

        var mainWindow = (MainWindow)viewFor;
        mainWindow.Show();
        MainWindow = mainWindow;

        // 性能诊断：连上节点但保持运行，供外部 curl 测时延/吞吐
        if (e.Args.Any(t => t == "--perf-connect"))
        {
            _ = Task.Run(async () => await Diagnostics.PerfConnect.ConnectAsync());
        }

        // 诊断探针：只写日志，不改行为。结果见 guiConfigs/uithread-db-probe.log
        if (e.Args.Any(t => t == "--db-probe"))
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Diagnostics.UiThreadDbProbe.RunAsync(Dispatcher);
                Dispatcher.Invoke(() => Shutdown(0));
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // 隐藏的自检入口：只给验证用，正常启动不受影响
        if (e.Args.Any(t => t == "--selftest"))
        {
            // 后台跑：数据库异步调用在 UI 线程上会挂起（本项目已踩过两次）
            _ = Task.Run(async () =>
            {
                var code = await Selftest.SelftestRunner.RunAsync(e.Args);
                // Shutdown 必须在 UI 线程执行，否则不会真正退出
                Dispatcher.Invoke(() => Shutdown(code));
            });
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logging.SaveLog("App_DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject != null)
        {
            Logging.SaveLog("CurrentDomain_UnhandledException", (Exception)e.ExceptionObject);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logging.SaveLog("OnExit");
        base.OnExit(e);
        Process.GetCurrentProcess().Kill();
    }
}
