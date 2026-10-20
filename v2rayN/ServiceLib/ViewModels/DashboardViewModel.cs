namespace ServiceLib.ViewModels;

/// <summary>
/// P2ray 主界面 ViewModel。
/// 承载三个页面（首页 / 节点 / 设置）所需的全部数据与命令。
///
/// 设计约束：
/// - 不注册任何 Interaction，避免与现有的 ProfilesView / StatusBarView 冲突。
/// - 命令一律委托给 StatusBarViewModel 或 ProfilesViewModel 的既有命令，不重复实现业务。
/// - 数据只读镜像，写入仍走上游原有流程。
/// </summary>
public partial class DashboardViewModel : MyReactiveObject
{
    private readonly StatusBarViewModel _statusBar;
    private readonly ProfilesViewModel _profiles;

    public DashboardViewModel(StatusBarViewModel statusBar, ProfilesViewModel profiles)
    {
        _statusBar = statusBar;
        _profiles = profiles;

        #region 连接状态镜像

        this.WhenAnyValue(x => x._statusBar.RunningServerDisplay)
            .Subscribe(v =>
            {
                // 未选节点时上游会塞入 ResUI.CheckServerSettings 作为提示，不应作为节点名展示。
                var isNoServer = v.IsNullOrEmpty() || v == ResUI.CheckServerSettings;
                HasServer = !isNoServer;

                // 上游给的是 "[VLESS] 名称(104.***.1:443)" 这种技术摘要。
                // 面向不懂网络的用户，只显示节点名（去掉协议前缀和地址尾巴）。
                RunningServerDisplay = isNoServer ? "还没有选择线路" : FriendlyNodeName(v);
            });

        this.WhenAnyValue(x => x._statusBar.RunningInfoDisplay)
            .Subscribe(v => RunningInfoDisplay = v ?? string.Empty);
        this.WhenAnyValue(x => x._statusBar.InboundDisplay)
            .Subscribe(v =>
            {
                InboundDisplay = v ?? string.Empty;
                InboundPortDisplay = FriendlyInboundPort(v);
            });
        this.WhenAnyValue(x => x._statusBar.InboundLanDisplay)
            .Subscribe(v => InboundLanDisplay = v ?? string.Empty);

        // 上游速度是复合串 "{标签} : {上}/s↑ | {下}/s↓"，这里拆成独立数值。
        this.WhenAnyValue(x => x._statusBar.SpeedProxyDisplay)
            .Subscribe(v =>
            {
                var (up, down) = ParseSpeedDisplay(v);
                UploadDisplay = up;
                DownloadDisplay = down;

                // 曲线需要数值而非格式化文本
                var raw = ParseSpeedBytes(v);
                PushSample(UploadHistory, raw.Up);
                PushSample(DownloadHistory, raw.Down);
            });

        // 核心名与连接时长随连接状态更新
        this.WhenAnyValue(x => x.IsConnected)
            .Subscribe(connected =>
            {
                if (!connected)
                {
                    RefreshConnectionHint(false);
                    CoreTypeDisplay = "—";
                    RunTimeDisplay = "—";
                    _connectedAt = null;
                    return;
                }

                _connectedAt ??= DateTime.Now;
                RefreshConnectionHint(true);

                // RunningCoreType 在 CoreManager.LoadCore 成功后才写入，
                // 而 IsConnected 由系统代理开关驱动，可能更早变为 true。
                // 延迟一拍再读，读不到则显示占位符而不是枚举默认值 0。
                Signal.Every(TimeSpan.FromSeconds(1))
                    .Take(1)
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Subscribe(_ =>
                    {
                        try
                        {
                            var running = AppManager.Instance.RunningCoreType;
                            CoreTypeDisplay = (int)running == 0 ? "—" : running.ToString();
                        }
                        catch
                        {
                            CoreTypeDisplay = "—";
                        }
                    });

                UpdateRunTime();
            });

        this.WhenAnyValue(
                x => x._statusBar.BlSystemProxySet,
                x => x._statusBar.BlSystemProxyPac,
                (set, pac) => set || pac)
            .Subscribe(connected => IsConnected = connected);

        // 上游 BlSystemProxySet 初值恒为 false，真实状态要等 SetListenerType 才写入。
        // 这里主动同步一次，避免启动后长期显示"未连接"。
        IsConnected = _statusBar.BlSystemProxySet || _statusBar.BlSystemProxyPac;

        #endregion

        #region 节点与订阅镜像

        this.WhenAnyValue(x => x._profiles.ProfileItems)
            .Subscribe(_ => Nodes = _profiles.ProfileItems);
        this.WhenAnyValue(x => x._profiles.SelectedProfile)
            .Subscribe(v => SelectedNode = v);

        // 反向同步：SelectedNode 由节点页 ListBox 驱动，必须写回 ProfilesViewModel.SelectedProfile，
        // 否则 SetDefaultServer() / RemoveServerAsync() 读到的仍是旧选择。
        this.WhenAnyValue(x => x.SelectedNode)
            .Where(v => v is not null)
            .Subscribe(v =>
            {
                if (!ReferenceEquals(_profiles.SelectedProfile, v))
                {
                    _profiles.SelectedProfile = v;
                }
            });
        // 不用 _profiles.SubItems：它含一个 Id 为空的"所有"虚拟分组，不是真实订阅。
        // 真实订阅列表由 LoadSubscriptionsAsync 从数据库读取。

        Nodes = _profiles.ProfileItems;
        SelectedNode = _profiles.SelectedProfile;

        #endregion

        #region 命令委托

        ConnectCmd = ReactiveCommand.CreateFromTask(async () =>
            await _statusBar.SystemProxySetCmd.Execute().FirstOrDefaultAsync());

        DisconnectCmd = ReactiveCommand.CreateFromTask(async () =>
            await _statusBar.SystemProxyClearCmd.Execute().FirstOrDefaultAsync());

        UpdateSubscriptionCmd = ReactiveCommand.CreateFromTask(async () =>
            await _statusBar.SubUpdateViaProxyCmd.Execute().FirstOrDefaultAsync());

        AddFromClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
            await _statusBar.AddServerViaClipboardCmd.Execute().FirstOrDefaultAsync());

        CopyProxyCmdCommand = ReactiveCommand.CreateFromTask(async () =>
            await _statusBar.CopyProxyCmdToClipboardCmd.Execute().FirstOrDefaultAsync());

        SetDefaultNodeCmd = ReactiveCommand.CreateFromTask(async () =>
            await _profiles.SetDefaultServerCmd.Execute().FirstOrDefaultAsync());

        RefreshNodesCmd = ReactiveCommand.CreateFromTask(async () =>
            await _profiles.RefreshServersBiz());

        #endregion

        _ = LoadSubscriptionsAsync();
    }

    /// <summary>
    /// 拉取订阅列表用于设置页展示（含上次更新时间）。
    /// </summary>
    public async Task LoadSubscriptionsAsync()
    {
        try
        {
            var lst = await AppManager.Instance.SubItems();
            Subscriptions = lst is null
                ? []
                : new BulkObservableCollection<SubItem>(lst);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("DashboardViewModel.LoadSubscriptionsAsync", ex);
        }
    }

    #region 连接状态

    [Reactive]
    public partial string RunningServerDisplay { get; set; }

    [Reactive]
    public partial string RunningInfoDisplay { get; set; }

    [Reactive]
    public partial string InboundDisplay { get; set; }

    /// <summary>
    /// 面向普通用户的本地端口，只保留数字。
    /// 上游 InboundDisplay 是 "本地:[mixed:10808]" 这种技术摘要，直接显示没人看得懂。
    /// </summary>
    [Reactive]
    public partial string InboundPortDisplay { get; set; } = "—";

    [Reactive]
    public partial string InboundLanDisplay { get; set; }

    [Reactive]
    public partial string UploadDisplay { get; set; }

    [Reactive]
    public partial string DownloadDisplay { get; set; }

    [Reactive]
    public partial bool IsConnected { get; set; }

    [Reactive]
    public partial bool HasServer { get; set; }

    /// <summary>当前运行的内核名。</summary>
    [Reactive]
    public partial string CoreTypeDisplay { get; set; }

    /// <summary>已连接时长的人类可读形式。</summary>
    [Reactive]
    public partial string RunTimeDisplay { get; set; }

    #endregion

    #region 节点与订阅

    [Reactive]
    public partial BulkObservableCollection<ProfileItemModel> Nodes { get; set; }

    [Reactive]
    public partial ProfileItemModel SelectedNode { get; set; }

    /// <summary>真实订阅列表（不含虚拟分组）。</summary>
    [Reactive]
    public partial BulkObservableCollection<SubItem> Subscriptions { get; set; }

    #endregion

    #region 命令

    public ReactiveCommand<RxVoid, RxVoid> ConnectCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> DisconnectCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> UpdateSubscriptionCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddFromClipboardCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> CopyProxyCmdCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SetDefaultNodeCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> RefreshNodesCmd { get; }

    /// <summary>删除选中节点。委托给 ProfilesViewModel.RemoveServerCmd。</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveNodeCmd => _profiles.RemoveServerCmd;

    /// <summary>编辑选中节点。</summary>
    public ReactiveCommand<RxVoid, RxVoid> EditNodeCmd => _profiles.EditServerCmd;

    /// <summary>
    /// 请求导航到节点页。由主窗口订阅后切换页面。
    /// </summary>
    public event Action? NavigateToNodesRequested;

    public void RequestNavigateToNodes()
    {
        NavigateToNodesRequested?.Invoke();
    }

    /// <summary>测试全部节点延迟。</summary>
    public ReactiveCommand<RxVoid, RxVoid> TestAllNodesCmd => _profiles.MixedTestServerCmd;

    /// <summary>测试选中节点延迟。</summary>
    public ReactiveCommand<RxVoid, RxVoid> TestNodeDelayCmd => _profiles.MixedTestServerCmd;

    /// <summary>
    /// 当前选中节点集合。
    /// ProfilesViewModel.RemoveServerAsync 依赖它，而 ListBox 不会自动写入，由视图层同步。
    /// </summary>
    public IList<ProfileItemModel>? SelectedProfiles
    {
        get => _profiles.SelectedProfiles;
        set => _profiles.SelectedProfiles = value!;
    }

    #endregion

    #region 面向普通用户的文案

    /// <summary>
    /// 把上游的技术摘要 "[VLESS] 名称(地址:端口)" 转成只有名称。
    /// 不懂网络的用户不关心协议名和地址，看到那串只会觉得复杂。
    /// </summary>
    private static string FriendlyNodeName(string? summary)
    {
        if (summary.IsNullOrEmpty())
        {
            return "还没有选择线路";
        }

        var s = summary.Trim();

        // 去掉开头的 [协议] 段
        if (s.StartsWith('['))
        {
            var close = s.IndexOf(']');
            if (close > 0 && close + 1 < s.Length)
            {
                s = s[(close + 1)..].Trim();
            }
        }

        // 去掉结尾的 (地址:端口) 段
        var lastOpen = s.LastIndexOf('(');
        if (lastOpen > 0 && s.EndsWith(')'))
        {
            s = s[..lastOpen].Trim();
        }

        return s.IsNullOrEmpty() ? summary : s;
    }

    /// <summary>
    /// 把上游的技术串 "本地:[mixed:10808]" / "本地:[mixed:10808,10809]" 提取成端口。
    /// 协议名对用户没有意义，只留下能填进其他软件的端口号。
    /// </summary>
    private static string FriendlyInboundPort(string? inbound)
    {
        if (inbound.IsNullOrEmpty())
        {
            return "—";
        }

        var open = inbound.IndexOf('[');
        var close = inbound.LastIndexOf(']');
        if (open < 0 || close <= open)
        {
            return "—";
        }

        var inner = inbound[(open + 1)..close];
        var ports = new List<string>();
        foreach (var part in inner.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.LastIndexOf(':');
            var port = (colon >= 0 ? part[(colon + 1)..] : part).Trim();
            if (port.Length > 0 && port.All(char.IsAsciiDigit))
            {
                ports.Add(port);
            }
        }

        return ports.Count > 0 ? string.Join(" / ", ports) : "—";
    }

    /// <summary>
    /// 连接状态的一句话说明，替代"系统代理已开启"这种术语。
    /// </summary>
    [Reactive]
    public partial string ConnectionHint { get; set; } = "点下面的按钮开始使用";

    private void RefreshConnectionHint(bool connected)
    {
        if (!connected)
        {
            ConnectionHint = HasServer ? "点下面的按钮开始使用" : "先去「线路」页挑一个，或从剪贴板导入订阅";
        }
        else
        {
            ConnectionHint = "已为你打开全局代理，现在可以正常上网了";
        }
    }

    #endregion

    #region 连接时长

    private DateTime? _connectedAt;
    private IDisposable? _runTimeTimer;

    private void UpdateRunTime()
    {
        if (_connectedAt is null)
        {
            RunTimeDisplay = "—";
            return;
        }

        var span = DateTime.Now - _connectedAt.Value;
        RunTimeDisplay = span.TotalHours >= 1
            ? $"已运行 {(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"已运行 {span.Minutes:D2}:{span.Seconds:D2}";

        // 惰性启动 1 秒计时器，仅在连接期间运行
        _runTimeTimer ??= Signal.Every(TimeSpan.FromSeconds(1))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => UpdateRunTime());
    }

    #endregion

    #region 速率曲线

    /// <summary>曲线采样点数。上游速度每秒更新一次，60 点即最近一分钟。</summary>
    private const int HistoryCapacity = 60;

    [Reactive]
    public partial BulkObservableCollection<double> UploadHistory { get; set; } = [];

    [Reactive]
    public partial BulkObservableCollection<double> DownloadHistory { get; set; } = [];

    private void PushSample(BulkObservableCollection<double> history, double value)
    {
        history.Add(value);
        while (history.Count > HistoryCapacity)
        {
            history.RemoveAt(0);
        }
    }

    /// <summary>
    /// 把 "mixed : 1.2 KB/s↑ | 3.4 MB/s↓" 解析为字节数，供曲线使用。
    /// </summary>
    private static (double Up, double Down) ParseSpeedBytes(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return (0, 0);
        }

        var parts = text.Split('|');
        return (ExtractBytes(parts.ElementAtOrDefault(0), '↑'),
                ExtractBytes(parts.ElementAtOrDefault(1), '↓'));
    }

    private static double ExtractBytes(string? segment, char arrow)
    {
        if (segment.IsNullOrEmpty())
        {
            return 0;
        }

        var idx = segment.IndexOf(arrow);
        var body = idx >= 0 ? segment[..idx] : segment;
        var colon = body.LastIndexOf(':');
        var value = (colon >= 0 ? body[(colon + 1)..] : body).Trim();
        if (value.IsNullOrEmpty())
        {
            return 0;
        }

        // 形如 "1.2 KB/s" / "512 B/s" / "3.40 MB/s"
        var space = value.IndexOf(' ');
        if (space <= 0)
        {
            return 0;
        }

        if (!double.TryParse(value[..space], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return 0;
        }

        var unit = value[(space + 1)..].Trim().ToUpperInvariant();
        var factor = unit switch
        {
            "KB/S" => 1024d,
            "MB/S" => 1024d * 1024,
            "GB/S" => 1024d * 1024 * 1024,
            _ => 1d
        };

        return number * factor;
    }

    #endregion

    #region 解析

    /// <summary>
    /// 解析速度复合串，例如 "mixed : 1.2 KB/s↑ | 3.4 MB/s↓"。
    /// 未启用实时速度或格式变化时回退为 0 B/s。
    /// </summary>
    private static (string Up, string Down) ParseSpeedDisplay(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return (ZeroSpeed, ZeroSpeed);
        }

        var parts = text.Split('|');
        return (ExtractValue(parts.ElementAtOrDefault(0), '↑'),
                ExtractValue(parts.ElementAtOrDefault(1), '↓'));
    }

    private const string ZeroSpeed = "0 B/s";

    private static string ExtractValue(string? segment, char arrow)
    {
        if (segment.IsNullOrEmpty())
        {
            return ZeroSpeed;
        }

        var idx = segment.IndexOf(arrow);
        var body = idx >= 0 ? segment[..idx] : segment;
        var colon = body.LastIndexOf(':');
        var value = (colon >= 0 ? body[(colon + 1)..] : body).Trim();

        return value.IsNullOrEmpty() ? ZeroSpeed : value;
    }

    #endregion
}