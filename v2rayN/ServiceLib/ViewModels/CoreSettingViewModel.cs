namespace ServiceLib.ViewModels;

/// <summary>
/// 内核设置：按协议指定用哪个内核跑。
///
/// 这是用户最常改的一项 —— 例如想用「按域名统计」就得把 VLESS 从 Xray 换成 sing-box。
/// 原来它埋在 1500 行的「参数设置」窗口里的一个页签，找起来很费劲，所以单独做一个窗口。
/// </summary>
public partial class CoreSettingViewModel : MyReactiveObject
{
    /// <summary>参与配置的协议。Outbound / PolicyGroup / ProxyChain 是内部类型，不暴露。</summary>
    private static readonly EConfigType[] _protocols =
    [
        EConfigType.VMess, EConfigType.VLESS, EConfigType.Shadowsocks, EConfigType.Trojan,
        EConfigType.Hysteria2, EConfigType.TUIC, EConfigType.Anytls, EConfigType.Naive,
        EConfigType.WireGuard, EConfigType.MASQUE, EConfigType.SOCKS, EConfigType.HTTP,
        EConfigType.Custom,
    ];

    public BulkObservableCollection<CoreSettingRow> Rows { get; } = [];

    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ResetCmd { get; }

    public CoreSettingViewModel()
    {
        _config = AppManager.Instance.Config;

        SaveCmd = ReactiveCommand.CreateFromTask(SaveAsync);
        ResetCmd = ReactiveCommand.CreateFromTask(ResetToXrayAsync);

        Load();
    }

    private void Load()
    {
        var map = _config.CoreTypeItem ?? [];
        Rows.Clear();
        foreach (var protocol in _protocols)
        {
            var current = map.FirstOrDefault(t => t.ConfigType == protocol)?.CoreType ?? ECoreType.Xray;
            Rows.Add(new CoreSettingRow
            {
                ConfigType = protocol,
                Protocol = ProtocolName(protocol),
                // 每行给一份独立副本：共用同一个 List 实例时，WPF 会让这些 ComboBox
                // 共享同一个 CollectionView，选择会互相串（点了不生效/选中被弹回）。
                Cores = [.. Global.CoreTypes],
                SelectedCore = current.ToString(),
            });
        }
    }

    /// <summary>协议名：直接用枚举名，与节点列表里的「协议」列一致。</summary>
    private static string ProtocolName(EConfigType type) => type switch
    {
        EConfigType.Custom => "自定义配置",
        EConfigType.SOCKS => "SOCKS",
        EConfigType.HTTP => "HTTP",
        _ => type.ToString(),
    };

    private async Task SaveAsync()
    {
        _config.CoreTypeItem ??= [];
        foreach (var row in Rows)
        {
            var coreType = Enum.TryParse<ECoreType>(row.SelectedCore, out var parsed) ? parsed : ECoreType.Xray;
            var item = _config.CoreTypeItem.FirstOrDefault(t => t.ConfigType == row.ConfigType);
            if (item is null)
            {
                _config.CoreTypeItem.Add(new CoreTypeItem { ConfigType = row.ConfigType, CoreType = coreType });
            }
            else
            {
                item.CoreType = coreType;
            }
        }

        await ConfigHandler.SaveConfig(_config);
        NoticeManager.Instance.Enqueue("已保存，重新连接后生效");

        // 内核是按协议映射的，改完必须重启内核才生效
        if (AppManager.Instance.RunningCoreType != default)
        {
            AppEvents.ReloadCoreRequested.Publish();
        }
    }

    private async Task ResetToXrayAsync()
    {
        foreach (var row in Rows)
        {
            row.SelectedCore = nameof(ECoreType.Xray);
        }

        await Task.CompletedTask;
    }
}

/// <summary>一条协议 → 内核的映射。</summary>
public partial class CoreSettingRow : ReactiveObject
{
    public EConfigType ConfigType { get; set; }

    public string Protocol { get; set; } = string.Empty;

    public IList<string> Cores { get; set; } = [];

    [Reactive]
    public partial string SelectedCore { get; set; } = string.Empty;
}
