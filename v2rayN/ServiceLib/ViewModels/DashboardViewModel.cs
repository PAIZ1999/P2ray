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
            .Subscribe(items =>
            {
                BindNodes(items);
                Nodes = items;
            });
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

        #region 节点页分组切换

        // 上游 SelectedSub 变化 → 首页镜像到 SelectedNodeGroup（含启动初值同步）
        this.WhenAnyValue(x => x._profiles.SelectedSub)
            .Subscribe(v =>
            {
                _suppressGroupSync = true;
                try
                {
                    SelectedNodeGroup = v;
                }
                finally
                {
                    _suppressGroupSync = false;
                }
            });

        // 反向：用户在节点页选了分组 → 写回上游，由它重载节点列表。
        // _suppressGroupSync 打断回环：上面写 SelectedNodeGroup 会再次触发这里。
        this.WhenAnyValue(x => x.SelectedNodeGroup)
            .Where(v => v is not null && !_suppressGroupSync)
            .Where(v => !ReferenceEquals(_profiles.SelectedSub, v)
                        && _profiles.SelectedSub?.Id != v.Id)
            .Subscribe(v =>
            {
                _profiles.SelectedSub = v;
            });

        #endregion

        Nodes = _profiles.ProfileItems;
        BindNodes(Nodes);
        SelectedNode = _profiles.SelectedProfile;

        #endregion

        #region 路由模式

        // 上游把路由列表存在 StatusBarViewModel.RoutingItems，切换动作是
        // SelectedRouting 变更 → RoutingSelectedChangedAsync → ReloadRequested。
        // 首页直接复用这条链路，不自己写 SetDefaultRouting，避免绕开重载逻辑。
        this.WhenAnyValue(x => x._statusBar.RoutingItems)
            .Subscribe(items =>
            {
                RoutingModes = items;

                // 兜底选中：上游 SelectedRouting 在冷启动时可能还没来得及赋值，
                // 于是分段控件一个都不高亮，看起来像"没启用任何分流"。
                // 这里按 IsActive 找回真正生效的那条，找不到就选第一条（规则）。
                if (SelectedRoutingMode is null && items is { Count: > 0 })
                {
                    var active = items.FirstOrDefault(r => r.IsActive) ?? items[0];
                    _suppressRoutingSync = true;
                    try
                    {
                        SelectedRoutingMode = active;
                    }
                    finally
                    {
                        _suppressRoutingSync = false;
                    }

                    UpdateRoutingHintText(active.Remarks);
                }
            });

        this.WhenAnyValue(x => x._statusBar.SelectedRouting)
            .Subscribe(v =>
            {
                // 用 _suppressRoutingSync 打断回环：首页赋值 → 上游赋值 → 首页再赋值…
                _suppressRoutingSync = true;
                try
                {
                    SelectedRoutingMode = v;
                }
                finally
                {
                    _suppressRoutingSync = false;
                }
            });

        RoutingModes = _statusBar.RoutingItems;
        SelectedRoutingMode = _statusBar.SelectedRouting;

        // 反向同步：首页下拉选新模式后写回上游，由它触发重载。
        this.WhenAnyValue(x => x.SelectedRoutingMode)
            .Where(v => v is not null && !_suppressRoutingSync)
            .Where(v => !ReferenceEquals(_statusBar.SelectedRouting, v))
            .Subscribe(v =>
            {
                // SelectedRouting 的 setter 会触发 RoutingSelectedChangedAsync，
                // 内部完成 SetDefaultRouting + ReloadRequested.Publish()。
                _statusBar.SelectedRouting = v;
            });

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

        // 网络探测：探测本身要跑十几个请求，用 ReactiveCommand 自带的重入保护，
        // 避免用户连点刷新按钮叠出多轮探测。
        RefreshNetProbeCmd = ReactiveCommand.CreateFromTask(RefreshNetProbeAsync);

        // 直连规则
        AddDirectRuleCmd = ReactiveCommand.CreateFromTask(AddDirectRuleAsync);
        OpenDirectRulePanelCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            IsDirectRulePanelOpen = !IsDirectRulePanelOpen;
            if (IsDirectRulePanelOpen)
            {
                await LoadDirectRulesAsync();
            }
        });

        // NetProbeSummary 是由两个计数和代理标记拼出来的，需要跟着它们一起通知，
        // 否则界面上的「x / y 可用」不会刷新。
        this.WhenAnyValue(x => x.NetProbeOkCount, x => x.NetProbeTotalCount, x => x.NetProbeViaProxy)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(NetProbeSummary)));

        #endregion

        _ = LoadSubscriptionsAsync();
    }

    /// <summary>
    /// 跑一轮网络探测：出口 IP + 归属地，然后逐个站点测连通性。
    /// 每次切换代理模式 / 节点后网络位置会变，视图层据此重新调用。
    /// </summary>
    public async Task RefreshNetProbeAsync()
    {
        if (_netProbeRunning)
        {
            return;
        }

        _netProbeRunning = true;
        try
        {
            IsNetProbeRunning = true;
            NetProbeStatus = "正在检测…";

            // 上一轮的数字必须立刻清零。
            // 探测要跑十几秒，这期间每个站点都是灰的"未测"，如果汇总还挂着
            // 上一轮的"5 / 10 可用"，用户会看到一个和下面格子对不上的数字，
            // 分不清它说的是现在还是刚才。
            NetProbeOkCount = 0;
            NetProbeTotalCount = 0;

            // 探测目标先按已知清单铺好占位行，用户能立刻看到要测哪些站点，
            // 而不是面对一块空白等十几秒。
            NetProbes = new BulkObservableCollection<NetProbeResult>(NetProbeService.CreatePlaceholders());

            // 分组也要跟着清空，否则上一轮的分组小结会挂在下面
            NetProbeGroups = [];

            var service = new NetProbeService();

            // 把本轮走的是代理还是直连记下来。未连接时境外站点当然可能全部"可用"，
            // 但那是本地网络直连的结果，不能和"代理已生效"混为一谈。
            NetProbeViaProxy = service.UsedProxy;

            var location = await service.GetIpLocationAsync();
            ApplyIpLocation(location);

            // 国内出口与站点探测并行：它走直连，与代理那条路径互不影响，
            // 串行执行会让面板多等一秒以上。
            var domesticTask = service.GetDomesticIpAsync();
            var scoreTask = location?.Ip is { Length: > 0 } ip
                ? service.GetIpScoreAsync(ip)
                : Task.FromResult<IpScoreResult?>(null);

            var results = await service.ProbeAllAsync();

            NetProbes = new BulkObservableCollection<NetProbeResult>(results);
            NetProbeOkCount = results.Count(r => r.Ok);
            NetProbeTotalCount = results.Count;
            RebuildNetProbeGroups();

            ApplyDomesticIpLocation(await domesticTask);
            ApplyIpScore(await scoreTask);

            NetProbeStatus = NetProbeUpdatedAt;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("DashboardViewModel.RefreshNetProbeAsync", ex);
            NetProbeStatus = "检测失败";
        }
        finally
        {
            _netProbeRunning = false;
            IsNetProbeRunning = false;
        }
    }

    private void ApplyIpLocation(IpLocationResult? info)
    {
        _ipLocation = info;

        if (info is null || string.IsNullOrEmpty(info.Ip))
        {
            IpAddressDisplay = "获取失败";
            IpLocationDisplay = string.Empty;
            IpCountryDisplay = string.Empty;
            IpFlagUri = null;
            ExitCountryCode = null;
            return;
        }

        IpAddressDisplay = info.Ip;
        IpLocationDisplay = NetProbeService.FormatLocation(info);
        IpCountryDisplay = info.CountryDisplay;
        // 素材里没有这个国家时返回 null，界面据此隐藏图片而不是显示破图。
        IpFlagUri = info.FlagImageUri;
        ExitCountryCode = info.CountryCode?.ToLowerInvariant();

        NetProbeUpdatedAt = $"更新于 {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>
    /// 套用国内出口。null 有两种含义，都应当收起这一行：
    /// 1. 当前就是直连 —— 国内出口与主出口是同一个地址，重复显示无意义；
    /// 2. 直连查询失败 —— 宁可只显示一条，也不要显示"获取失败"占着位置。
    /// </summary>
    private void ApplyDomesticIpLocation(IpLocationResult? info)
    {
        if (info is null || string.IsNullOrEmpty(info.Ip))
        {
            HasDomesticExit = false;
            DomesticIpAddressDisplay = "—";
            DomesticIpLocationDisplay = string.Empty;
            DomesticIpCountryDisplay = string.Empty;
            DomesticIpFlagUri = null;
            return;
        }

        HasDomesticExit = true;
        DomesticIpAddressDisplay = info.Ip;
        DomesticIpLocationDisplay = NetProbeService.FormatLocation(info);
        DomesticIpCountryDisplay = info.CountryDisplay;
        DomesticIpFlagUri = info.FlagImageUri;
    }

    /// <summary>
    /// 把评分结果映射到界面字段。拿不到评分时整块隐藏，
    /// 而不是把 0 分显示出来 —— 0 分会被读成"这个 IP 极差"，与"没查到"是两回事。
    /// </summary>
    private void ApplyIpScore(IpScoreResult? score)
    {
        _ipScore = score;

        if (score is null)
        {
            HasIpScore = false;
            IpScoreDisplay = "—";
            IpScoreLevel = string.Empty;
            IpTypeDisplay = string.Empty;
            IpAsnDisplay = string.Empty;
            IpRiskDisplay = string.Empty;
            IpAiVerdictDisplay = string.Empty;
            return;
        }

        HasIpScore = true;
        IpScoreValue = score.TrustScore;
        IpScoreDisplay = $"{score.TrustScore}";
        IpScoreLevel = score.TrustLevel;
        IpTypeDisplay = score.IpTypeText;

        // ASN 与组织拼一行；组织名可能很长，界面负责截断
        IpAsnDisplay = score.Asn.IsNullOrEmpty()
            ? score.AsOrganization ?? string.Empty
            : $"{score.Asn} {score.AsOrganization}".Trim();

        IpRiskDisplay = score.RiskText;

        IpAiVerdictDisplay = score.AiVerdictLabel.IsNullOrEmpty()
            ? string.Empty
            : $"{score.AiVerdictLabel} · {score.AiVerdictConfidence}%";
    }

    /// <summary>
    /// 拉取订阅列表用于设置页展示（含上次更新时间），同时刷新节点页的分组下拉。
    /// </summary>
    public async Task LoadSubscriptionsAsync()
    {
        try
        {
            var lst = await AppManager.Instance.SubItems() ?? [];

            // 首页订阅列表只认真实订阅
            Subscriptions = new BulkObservableCollection<SubItem>(lst);

            // 节点页需要「所有」这一项，它的 Id 为空，代表不按订阅过滤。
            // 用上游同一份副本，避免把虚拟项写回数据库。
            var groups = new List<SubItem> { new() { Remarks = ResUI.AllGroupServers } };
            groups.AddRange(lst);
            NodeGroups = new BulkObservableCollection<SubItem>(groups);

            // 选中项跟随当前过滤条件：SubIndexId 为空时对应「所有」
            var current = _profiles.SelectedSub;
            var target = current is not null && groups.Any(g => g.Id == current.Id)
                ? groups.First(g => g.Id == current.Id)
                : groups[0];

            _suppressGroupSync = true;
            try
            {
                SelectedNodeGroup = target;
            }
            finally
            {
                _suppressGroupSync = false;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("DashboardViewModel.LoadSubscriptionsAsync", ex);
        }
    }

    #region 网络位置与连通性

    /// <summary>
    /// 出口 IP —— 跟随当前路径。连了代理时这里是境外出口，
    /// 没连接时与 DomesticIpAddressDisplay 相同。
    /// </summary>
    [Reactive]
    public partial string IpAddressDisplay { get; set; } = "—";

    [Reactive]
    public partial string IpLocationDisplay { get; set; }

    /// <summary>国旗图片地址（pack URI）。null 表示没有素材，界面应隐藏图片。</summary>
    [Reactive]
    public partial string IpFlagUri { get; set; }

    /// <summary>国家中文名，单独暴露以便在旗帜旁显示文字。</summary>
    [Reactive]
    public partial string IpCountryDisplay { get; set; }

    /// <summary>当前出口的国家代码（小写），用于判断是否需要拆成"境外 + 国内"两行。</summary>
    [Reactive]
    public partial string ExitCountryCode { get; set; }

    #region 国内出口（不走代理时的真实出口）

    /// <summary>不走代理时的出口 IP。走代理时它才是"我的真实地址"。</summary>
    [Reactive]
    public partial string DomesticIpAddressDisplay { get; set; } = "—";

    [Reactive]
    public partial string DomesticIpLocationDisplay { get; set; }

    [Reactive]
    public partial string DomesticIpFlagUri { get; set; }

    [Reactive]
    public partial string DomesticIpCountryDisplay { get; set; }

    /// <summary>是否显示国内出口这一行。直连状态下它与主出口重复，不必显示。</summary>
    [Reactive]
    public partial bool HasDomesticExit { get; set; }

    #endregion

    #region IP 评分

    /// <summary>是否拿到了评分。false 时界面隐藏评分区，不显示 0 分。</summary>
    [Reactive]
    public partial bool HasIpScore { get; set; }

    /// <summary>信任度数值，用于着色（越高越绿）。</summary>
    [Reactive]
    public partial int IpScoreValue { get; set; }

    [Reactive]
    public partial string IpScoreDisplay { get; set; } = "—";

    /// <summary>信任度等级文案：优秀 / 良好 / 一般 / 较差 / 很差。</summary>
    [Reactive]
    public partial string IpScoreLevel { get; set; }

    /// <summary>IP 类型：住宅 IP / 机房 IP / 移动网络 / Tor 出口。</summary>
    [Reactive]
    public partial string IpTypeDisplay { get; set; }

    /// <summary>ASN 与归属组织，如 "AS979 NetLab Global"。</summary>
    [Reactive]
    public partial string IpAsnDisplay { get; set; }

    /// <summary>风险标记汇总，如 "机房 · VPN"。无风险时为空。</summary>
    [Reactive]
    public partial string IpRiskDisplay { get; set; }

    /// <summary>AI 判定，如 "Residential-like · 70%"。</summary>
    [Reactive]
    public partial string IpAiVerdictDisplay { get; set; }

    private IpScoreResult? _ipScore;

    #endregion

    [Reactive]
    public partial BulkObservableCollection<NetProbeResult> NetProbes { get; set; } = [];

    [Reactive]
    public partial bool IsNetProbeRunning { get; set; }

    [Reactive]
    public partial string NetProbeStatus { get; set; } = "尚未检测";

    [Reactive]
    public partial string NetProbeUpdatedAt { get; set; }

    [Reactive]
    public partial int NetProbeOkCount { get; set; }

    [Reactive]
    public partial int NetProbeTotalCount { get; set; }

    /// <summary>本轮探测是否走代理。false 表示这些结果是直连测出来的。</summary>
    [Reactive]
    public partial bool NetProbeViaProxy { get; set; }

    /// <summary>
    /// 连通性汇总文本，如「7 / 10 可用」。
    /// 直连测得的数字后面缀上「直连」，避免用户把"本地网络能上"误读成"代理已经工作"。
    /// </summary>
    public string NetProbeSummary => NetProbeTotalCount > 0
        ? $"{NetProbeOkCount} / {NetProbeTotalCount} 可用" + (NetProbeViaProxy ? string.Empty : "（直连）")
        : "—";

    public ReactiveCommand<RxVoid, RxVoid> RefreshNetProbeCmd { get; }

    private bool _netProbeRunning;
    private IpLocationResult? _ipLocation;

    /// <summary>
    /// 按分组投影的探测结果，供界面分组显示（国际 / AI / 国内）。
    ///
    /// 为什么不直接在 XAML 用 CollectionViewSource 分组：
    /// 这里要的不只是分组标题，还要每组自己的"x / y 可用"小计，
    /// 而 CollectionView 的 GroupItem 拿不到组内聚合值。
    /// </summary>
    [Reactive]
    public partial BulkObservableCollection<NetProbeGroup> NetProbeGroups { get; set; } = [];

    /// <summary>
    /// 把扁平的 NetProbes 按 Tag 组装成分组。
    /// 顺序固定为 国际 → AI → 国内，不随结果变化跳来跳去。
    /// </summary>
    private void RebuildNetProbeGroups()
    {
        var order = new[] { "国际", "AI", "国内" };
        var groups = new List<NetProbeGroup>(order.Length);

        foreach (var tag in order)
        {
            var items = NetProbes.Where(r => r.Tag == tag).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            groups.Add(new NetProbeGroup
            {
                Tag = tag,
                Items = new BulkObservableCollection<NetProbeResult>(items),
                OkCount = items.Count(r => r.Ok),
                TotalCount = items.Count,
                // 只统计成功项的均值：失败项没有延迟可言，
                // 按 0 计入会把平均值拉低，看起来像"线路很快"。
                AverageMs = items.Where(r => r.Ok && r.LatencyMs > 0).Select(r => r.LatencyMs).DefaultIfEmpty(0).Average(),
            });
        }

        NetProbeGroups = new BulkObservableCollection<NetProbeGroup>(groups);
    }

    #endregion

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

    /// <summary>
    /// 节点数量。不能直接用 Nodes.Count 做绑定：
    /// Nodes 和 ProfilesViewModel.ProfileItems 是同一个实例，刷新时内容被原地
    /// ReplaceRange，属性本身从未换过引用，WhenAnyValue(x => x.Nodes) 不会再触发，
    /// 界面会一直停在首次的 0 条。这里改为监听集合自身的变更来更新计数。
    /// </summary>
    [Reactive]
    public partial int NodeCount { get; set; }

    private void BindNodes(BulkObservableCollection<ProfileItemModel>? items)
    {
        if (_boundNodes == items)
        {
            return;
        }

        _nodesChanged?.Dispose();
        _nodesChanged = null;
        _boundNodes = items;

        if (items is null)
        {
            NodeCount = 0;
            return;
        }

        _nodesChanged = items
            .ObserveCollectionChanges()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => NodeCount = items.Count);

        NodeCount = items.Count;
    }

    private BulkObservableCollection<ProfileItemModel>? _boundNodes;
    private IDisposable? _nodesChanged;

    [Reactive]
    public partial ProfileItemModel SelectedNode { get; set; }

    /// <summary>真实订阅列表（不含虚拟分组）。</summary>
    [Reactive]
    public partial BulkObservableCollection<SubItem> Subscriptions { get; set; }

    /// <summary>
    /// 节点页的分组切换项。
    /// 第一项是上游的「所有」虚拟分组（Id 为空），其余是真实订阅。
    /// Subscriptions 会被首页订阅列表复用，不能往里塞虚拟项，
    /// 否则首页会多出一个点不动的"所有"条目，所以这里单独一份。
    /// </summary>
    [Reactive]
    public partial BulkObservableCollection<SubItem> NodeGroups { get; set; }

    /// <summary>
    /// 节点页当前选中的分组。
    /// 写入即切换：委托给 ProfilesViewModel.SelectedSub，
    /// 由它走上游的 SubSelectedChangedAsync 重载节点列表。
    /// </summary>
    [Reactive]
    public partial SubItem SelectedNodeGroup { get; set; }

    /// <summary>防止 SelectedNodeGroup 与上游 SelectedSub 互相赋值形成回环。</summary>
    private bool _suppressGroupSync;

    #endregion

    #region 路由模式

    /// <summary>可选的路由模式（绕过大陆 / 黑名单 / 全局 等）。</summary>
    [Reactive]
    public partial BulkObservableCollection<RoutingItem> RoutingModes { get; set; }

    /// <summary>
    /// 当前生效的路由模式。
    /// 写入即切模式：委托给 StatusBarViewModel.SelectedRouting，
    /// 由它负责 SetDefaultRouting + 重载内核。
    /// </summary>
    [Reactive]
    public partial RoutingItem SelectedRoutingMode { get; set; }

    /// <summary>防止 SelectedRoutingMode 与上游 SelectedRouting 互相赋值形成回环。</summary>
    private bool _suppressRoutingSync;

    /// <summary>
    /// 路由模式下面那行说明文案。
    /// 放在 ViewModel 而不是视图里，是因为切换来源有两处（用户点选、启动时兜底选中），
    /// 文案只能有一份，否则两处各写一套迟早写岔。
    /// </summary>
    [Reactive]
    public partial string RoutingHintText { get; set; } = "决定哪些网站的流量经过代理。";

    private void UpdateRoutingHintText(string? remarks)
    {
        var name = RoutingNameHelper.ToShortName(remarks);
        RoutingHintText = name switch
        {
            "全局" => "全部网站都走代理。速度取决于线路，流量消耗也最大。",
            "黑名单" => "只有列表内的网站走代理，其余直连。适合明确知道要访问哪些站点的场景。",
            "规则" => "国内网站直连，境外网站走代理。日常使用推荐这一项。",
            _ => "决定哪些网站的流量经过代理。",
        };
    }

    #endregion

    #region 自定义直连规则

    /// <summary>用户自己添加的直连条目（只含本界面管理的那些）。</summary>
    [Reactive]
    public partial BulkObservableCollection<DirectRuleEntry> DirectRules { get; set; } = [];

    /// <summary>输入框内容。填域名或 IP。</summary>
    [Reactive]
    public partial string DirectRuleInput { get; set; }

    /// <summary>操作结果提示（成功或失败），空串表示不显示。</summary>
    [Reactive]
    public partial string DirectRuleStatus { get; set; }

    /// <summary>让直连规则区展开/收起。默认收起，避免首页一上来就堆满设置项。</summary>
    [Reactive]
    public partial bool IsDirectRulePanelOpen { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> AddDirectRuleCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> OpenDirectRulePanelCmd { get; }

    private readonly DirectRuleService _directRuleService = new();

    /// <summary>刷新直连规则列表。</summary>
    public async Task LoadDirectRulesAsync()
    {
        try
        {
            var list = await _directRuleService.GetCustomRulesAsync();
            DirectRules = new BulkObservableCollection<DirectRuleEntry>(list);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("DashboardViewModel.LoadDirectRulesAsync", ex);
        }
    }

    /// <summary>加一条规则。成功后清空输入框并刷新列表。</summary>
    public async Task AddDirectRuleAsync()
    {
        var input = DirectRuleInput;
        if (string.IsNullOrWhiteSpace(input))
        {
            DirectRuleStatus = "请填写域名或 IP";
            return;
        }

        var error = await _directRuleService.AddAsync(input);
        if (error is not null)
        {
            DirectRuleStatus = error;
            return;
        }

        var value = input.Trim();
        DirectRuleInput = string.Empty;
        DirectRuleStatus = $"已添加直连：{value}";
        await LoadDirectRulesAsync();
    }

    /// <summary>删掉一条规则。</summary>
    public async Task RemoveDirectRuleAsync(string ruleId)
    {
        var error = await _directRuleService.RemoveAsync(ruleId);
        DirectRuleStatus = error ?? "已移除";
        await LoadDirectRulesAsync();
    }

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