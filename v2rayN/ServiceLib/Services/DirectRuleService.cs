namespace ServiceLib.Services;

/// <summary>
/// 让用户用一句话给某个站点/网段设置"直连"。
///
/// 为什么不直接复用上游的路由设置界面：那里列出的是整张规则表
/// （block udp443 / geosite:google / geoip:cn…），对只想"让某个网站直连"
/// 的用户来说完全是噪音，还得先理解 outboundTag、geosite 这些概念。
///
/// 这里只暴露一个输入框：填域名或 IP，就写成一条 direct 规则，
/// 插到当前生效的路由规则最前面（优先于后面按 geosite 分的段）。
/// </summary>
public class DirectRuleService
{
    private static readonly string _tag = "DirectRuleService";

    /// <summary>用户自己维护的直连规则统一用这个前缀标记，便于识别与撤销。</summary>
    public const string ManagedRemarksPrefix = "自定义直连: ";

    /// <summary>当前生效的路由项。没有则为 null。</summary>
    public async Task<RoutingItem?> GetActiveRoutingAsync()
    {
        try
        {
            return await ConfigHandler.GetDefaultRouting(AppManager.Instance.Config);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.GetActiveRoutingAsync", ex);
            return null;
        }
    }

    /// <summary>
    /// 读出当前路由里用户自己加的直连条目。
    /// 只返回 ManagedRemarksPrefix 开头的规则 —— 内置规则不归这个界面管，
    /// 列出来会让用户以为可以随便删，一删就把"绕过大陆"的基础逻辑弄坏了。
    /// </summary>
    public async Task<List<DirectRuleEntry>> GetCustomRulesAsync()
    {
        var routing = await GetActiveRoutingAsync();
        if (routing?.RuleSet.IsNullOrEmpty() != false)
        {
            return [];
        }

        var list = new List<DirectRuleEntry>();
        var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? [];

        foreach (var rule in rules)
        {
            if (rule.Remarks?.StartsWith(ManagedRemarksPrefix, StringComparison.Ordinal) != true)
            {
                continue;
            }

            // 一条自定义规则只装一个目标，所以取第一个即可
            var value = rule.Domain?.FirstOrDefault() ?? rule.Ip?.FirstOrDefault();
            if (value.IsNullOrEmpty())
            {
                continue;
            }

            list.Add(new DirectRuleEntry
            {
                Id = rule.Id,
                Value = value,
                IsDomain = rule.Domain?.Count > 0,
            });
        }

        return list;
    }

    /// <summary>
    /// 加一条直连规则。返回错误信息，null 表示成功。
    ///
    /// 判定输入类型：能解析成 IP 就写 Ip，否则按域名写 Domain。
    /// 域名统一加 "domain:" 前缀 —— 不加的话 xray 会当成关键词做子串匹配，
    /// 用户填 example.com 会连 notexample.com 一起命中。
    /// </summary>
    public async Task<string?> AddAsync(string rawInput)
    {
        var value = NormalizeInput(rawInput);
        if (value.IsNullOrEmpty())
        {
            return "请填写域名或 IP";
        }

        var routing = await GetActiveRoutingAsync();
        if (routing is null)
        {
            return "没有找到生效的路由配置";
        }

        var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? [];

        var isIp = IPAddress.TryParse(value, out _);
        var domainValue = isIp ? null : (value.StartsWith("domain:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("full:", StringComparison.OrdinalIgnoreCase)
                ? value
                : $"domain:{value}");

        // 去重：用户重复填同一个目标时不该多出一条一模一样的规则
        var duplicated = rules.Any(r =>
            r.Remarks?.StartsWith(ManagedRemarksPrefix, StringComparison.Ordinal) == true
            && (isIp
                ? r.Ip?.Any(i => string.Equals(i, value, StringComparison.OrdinalIgnoreCase)) == true
                : r.Domain?.Any(d => string.Equals(d, domainValue, StringComparison.OrdinalIgnoreCase)) == true));

        if (duplicated)
        {
            return $"{value} 已经在直连列表里了";
        }

        var rule = new RulesItem
        {
            Id = Utils.GetGuid(false),
            OutboundTag = Global.DirectTag,
            Enabled = true,
            Remarks = $"{ManagedRemarksPrefix}{value}",
        };

        if (isIp)
        {
            rule.Ip = [value];
        }
        else
        {
            rule.Domain = [domainValue];
        }

        // 插到最前面：自定义意图应当优先于内置的 geosite:cn / geosite:gfw 判定。
        // 追加到末尾会被前面那些规则先命中，用户会觉得"设了没用"。
        rules.Insert(0, rule);

        return await SaveAsync(routing, rules);
    }

    /// <summary>按 Id 删掉一条自定义直连规则。</summary>
    public async Task<string?> RemoveAsync(string ruleId)
    {
        var routing = await GetActiveRoutingAsync();
        if (routing is null)
        {
            return "没有找到生效的路由配置";
        }

        var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? [];
        var target = rules.FirstOrDefault(r => r.Id == ruleId
            && r.Remarks?.StartsWith(ManagedRemarksPrefix, StringComparison.Ordinal) == true);
        if (target is null)
        {
            return null;
        }

        rules.Remove(target);
        return await SaveAsync(routing, rules);
    }

    private static async Task<string?> SaveAsync(RoutingItem routing, List<RulesItem> rules)
    {
        routing.RuleSet = JsonUtils.Serialize(rules, false);
        routing.RuleNum = rules.Count;

        var ret = await ConfigHandler.SaveRoutingItem(AppManager.Instance.Config, routing);
        if (ret != 0)
        {
            return "保存失败";
        }

        // 规则改了必须重载内核，否则新规则只在下次启动才生效，用户会以为设置没起作用。
        // 这里不直接调 MainWindowViewModel（它是 UI 层的单例，ServiceLib 不该反向依赖），
        // 改为抛事件由主窗口接住并 Reload。
        RoutingRulesChanged?.Invoke();
        return null;
    }

    /// <summary>路由规则被修改。主窗口订阅后触发 Reload，让新规则立即生效。</summary>
    public static event Action? RoutingRulesChanged;

    /// <summary>
    /// 清理用户输入：去掉协议前缀、路径、端口、首尾空白。
    /// 用户经常直接从浏览器地址栏粘贴 https://example.com/path?x=1 这种。
    /// </summary>
    private static string NormalizeInput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var s = raw.Trim();

        // 协议前缀
        var schemeIdx = s.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx > 0)
        {
            s = s[(schemeIdx + 3)..];
        }

        // 路径与查询串
        var slashIdx = s.IndexOfAny(['/', '?', '#']);
        if (slashIdx > 0)
        {
            s = s[..slashIdx];
        }

        // 端口（IPv6 里冒号是合法字符，只有单个冒号才当端口分隔符）
        var colonCount = s.Count(c => c == ':');
        if (colonCount == 1)
        {
            var colonIdx = s.IndexOf(':');
            if (int.TryParse(s[(colonIdx + 1)..], out _))
            {
                s = s[..colonIdx];
            }
        }

        return s.Trim().TrimEnd('.').ToLowerInvariant();
    }
}

/// <summary>界面展示用的一条自定义直连规则。</summary>
public class DirectRuleEntry
{
    public string Id { get; set; } = string.Empty;

    /// <summary>域名或 IP。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>true 表示域名规则，false 表示 IP 规则。</summary>
    public bool IsDomain { get; set; }

    /// <summary>界面标签，区分域名与 IP 两种类型。</summary>
    public string KindText => IsDomain ? "域名" : "IP";
}
