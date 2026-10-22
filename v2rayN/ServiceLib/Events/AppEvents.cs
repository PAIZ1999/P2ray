namespace ServiceLib.Events;

public static class AppEvents
{
    public static readonly EventChannel<RxVoid> AddServerViaClipboardRequested = new();
    public static readonly EventChannel<bool> HasUpdateNotified = new();

    public static readonly EventChannel<ServerSpeedItem> DispatcherStatisticsRequested = new();

    public static readonly EventChannel<string> SendSnackMsgRequested = new();
    public static readonly EventChannel<string> SendMsgViewRequested = new();

    public static readonly EventChannel<RxVoid> AppExitRequested = new();
    public static readonly EventChannel<bool> ShutdownRequested = new();

    public static readonly EventChannel<ESysProxyType> SysProxyChangeRequested = new();

    /// <summary>
    /// 请求重载内核（重新生成配置并重启进程）。
    ///
    /// 改本地端口、改路由这类改动必须让内核重启才生效，而 Reload 在
    /// MainWindowViewModel 上。ServiceLib 里的 ViewModel 不该反向依赖主窗口，
    /// 所以走事件：发起方只声明"需要重载"，由主窗口接住并执行。
    /// </summary>
    public static readonly EventChannel<RxVoid> ReloadCoreRequested = new();
}
