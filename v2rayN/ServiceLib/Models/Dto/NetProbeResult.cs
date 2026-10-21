namespace ServiceLib.Models.Dto;

/// <summary>
/// 出口 IP 的归属地信息。
/// 字段与 ip.net.coffee 的 /api/geoip/{ip} 返回对应，缺失时保持 null，
/// 由界面决定显示占位符。
/// </summary>
public class IpLocationResult
{
    /// <summary>出口 IP。</summary>
    public string? Ip { get; set; }

    /// <summary>国家名，数据源返回英文，如 "China"。</summary>
    public string? Country { get; set; }

    /// <summary>两位国家代码，如 "cn"。</summary>
    public string? CountryCode { get; set; }

    /// <summary>省 / 州，如 "Zhejiang"。</summary>
    public string? Region { get; set; }

    /// <summary>城市，如 "Ningbo"。</summary>
    public string? City { get; set; }

    /// <summary>运营商，如 "China Mobile"。</summary>
    public string? Isp { get; set; }

    /// <summary>
    /// 数据源是否已直接返回中文（ip-api 的 lang=zh-CN）。
    /// 为 true 时不再套中文映射表，否则会把"中国"覆盖成查表结果。
    /// </summary>
    public bool AlreadyLocalized { get; set; }

    /// <summary>
    /// 国家代码对应的中文名。数据源只给英文，而界面是中文的，
    /// 直接显示 "United States California" 与整体语言不一致。
    /// </summary>
    private static readonly Dictionary<string, string> CountryNamesZh = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CN"] = "中国", ["HK"] = "中国香港", ["MO"] = "中国澳门", ["TW"] = "中国台湾",
        ["JP"] = "日本", ["KR"] = "韩国", ["SG"] = "新加坡", ["MY"] = "马来西亚",
        ["TH"] = "泰国", ["VN"] = "越南", ["ID"] = "印度尼西亚", ["PH"] = "菲律宾",
        ["IN"] = "印度", ["PK"] = "巴基斯坦", ["BD"] = "孟加拉国", ["LK"] = "斯里兰卡",
        ["KH"] = "柬埔寨", ["LA"] = "老挝", ["MM"] = "缅甸",
        ["US"] = "美国", ["CA"] = "加拿大", ["MX"] = "墨西哥", ["BR"] = "巴西",
        ["AR"] = "阿根廷", ["CL"] = "智利", ["CO"] = "哥伦比亚",
        ["GB"] = "英国", ["DE"] = "德国", ["FR"] = "法国", ["IT"] = "意大利",
        ["ES"] = "西班牙", ["RU"] = "俄罗斯", ["NL"] = "荷兰", ["CH"] = "瑞士",
        ["SE"] = "瑞典", ["NO"] = "挪威", ["DK"] = "丹麦", ["FI"] = "芬兰",
        ["PL"] = "波兰", ["CZ"] = "捷克", ["AT"] = "奥地利", ["GR"] = "希腊",
        ["PT"] = "葡萄牙", ["TR"] = "土耳其", ["UA"] = "乌克兰", ["RO"] = "罗马尼亚",
        ["IE"] = "爱尔兰", ["BE"] = "比利时", ["HU"] = "匈牙利", ["BG"] = "保加利亚",
        ["AE"] = "阿联酋", ["SA"] = "沙特", ["IL"] = "以色列", ["KZ"] = "哈萨克斯坦",
        ["IR"] = "伊朗", ["AU"] = "澳大利亚", ["NZ"] = "新西兰",
        ["ZA"] = "南非", ["EG"] = "埃及", ["NG"] = "尼日利亚", ["KE"] = "肯尼亚",
    };

    /// <summary>国家的中文名；未知代码回退到数据源给的英文名。</summary>
    public string CountryDisplay
    {
        get
        {
            // 数据源已经是中文时直接用，避免把「中国」查表成英文再翻回来
            if (AlreadyLocalized)
            {
                return Country ?? string.Empty;
            }

            return CountryCode is not null && CountryNamesZh.TryGetValue(CountryCode, out var zh)
                ? zh
                : Country ?? string.Empty;
        }
    }

    /// <summary>
    /// 国旗图片的 pack URI。素材放在 v2rayN 项目的 Resources/Flags，
    /// ServiceLib 不引用 UI 程序集，因此这里只拼字符串，由界面决定能否加载。
    /// 返回 null 表示没有对应素材，界面应退回文字标签。
    /// </summary>
    public string? FlagImageUri
    {
        get
        {
            if (CountryCode.IsNullOrEmpty() || CountryCode.Length != 2)
            {
                return null;
            }

            var cc = CountryCode.ToLowerInvariant();
            if (!CountryNamesZh.ContainsKey(CountryCode))
            {
                return null;
            }

            return $"pack://application:,,,/P2ray;component/Resources/Flags/{cc}.png";
        }
    }
}

/// <summary>单个站点的一次连通性探测结果。</summary>
public class NetProbeResult
{
    /// <summary>站点名，用于界面显示。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>分组标签：国内 / 国际 / AI。</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>图标素材名（Resources/Sites/{Slug}.png），没有素材时为 null。</summary>
    public string? Slug { get; set; }

    /// <summary>
    /// 图标图片地址（pack URI）。素材是内置资源，断网也能显示 ——
    /// 连通性面板恰恰是断网时最需要看的界面，图标不能依赖网络。
    /// </summary>
    public string? IconUri => string.IsNullOrEmpty(Slug)
        ? null
        : $"pack://application:,,,/P2ray;component/Resources/Sites/{Slug}.png";

    /// <summary>是否连通（HTTP 2xx）。</summary>
    public bool Ok { get; set; }

    /// <summary>
    /// 是否已经测过。
    ///
    /// 探测要串行跑十几个站点，界面一开始铺的是占位行。占位行的 Ok 也是 false，
    /// 光看 Ok 会把"还没轮到"和"真的连不上"显示成同一个红字"失败" ——
    /// 用户看到满屏失败会以为网络断了，其实只是还没测到。
    /// </summary>
    public bool HasResult { get; set; }

    /// <summary>耗时，毫秒。失败时为 0。</summary>
    public int LatencyMs { get; set; }

    /// <summary>失败原因，成功时为 null。</summary>
    public string? Error { get; set; }

    /// <summary>界面用的延迟文本。三种状态必须能分辨：未测 / 失败 / 具体毫秒。</summary>
    public string LatencyText => !HasResult ? "—" : Ok ? $"{LatencyMs}ms" : "失败";

    /// <summary>
    /// 给颜色转换器用：null 表示还没测（灰），true 成功（绿），false 失败（红）。
    /// 用可空布尔是因为三态用两个字段表达会让绑定写得很绕。
    /// </summary>
    public bool? State => !HasResult ? null : Ok;
}

/// <summary>
/// 出口 IP 的风险评估，数据来自 ip.net.coffee 的 /api/ip/lookup/{ip}。
///
/// 为什么值得单独做：连通性只回答"通不通"，而用户真正踩坑的是
/// "能上但被风控" —— 机房 IP 被 ChatGPT 拒、共享 IP 被 Reddit 封。
/// trust_score 与 is_datacenter 这类字段正好补上这一层。
/// </summary>
public class IpScoreResult
{
    /// <summary>综合信任度 0-100，越高越干净。</summary>
    public int TrustScore { get; set; }

    /// <summary>是否机房 IP。机房 IP 更容易被流媒体与 AI 站点拒绝。</summary>
    public bool IsDatacenter { get; set; }

    /// <summary>是否住宅 IP（对面给的判定，比单纯看 ASN 更宽松）。</summary>
    public bool IsResidential { get; set; }

    /// <summary>是否被标记为 VPN。</summary>
    public bool IsVpn { get; set; }

    /// <summary>是否代理。</summary>
    public bool IsProxy { get; set; }

    /// <summary>是否 Tor 出口。</summary>
    public bool IsTor { get; set; }

    /// <summary>是否滥用源（发垃圾 / 攻击的高危段）。</summary>
    public bool IsAbuser { get; set; }

    /// <summary>是否移动网络。</summary>
    public bool IsMobile { get; set; }

    /// <summary>ASN 编号。</summary>
    public string? Asn { get; set; }

    /// <summary>ASN 归属组织。</summary>
    public string? AsOrganization { get; set; }

    /// <summary>IP 所属公司名。</summary>
    public string? CompanyName { get; set; }

    /// <summary>机房名，非机房为空。</summary>
    public string? DatacenterName { get; set; }

    /// <summary>反向解析域名。</summary>
    public string? Rdns { get; set; }

    /// <summary>AI 判定标签，如 "Residential-like" / "Hosting"。</summary>
    public string? AiVerdictLabel { get; set; }

    /// <summary>AI 判定置信度 0-100。</summary>
    public int AiVerdictConfidence { get; set; }

    /// <summary>AI 判定理由（英文原文）。</summary>
    public string? AiVerdictReason { get; set; }

    /// <summary>信任度的文案等级，用于界面着色。</summary>
    public string TrustLevel => TrustScore switch
    {
        >= 80 => "优秀",
        >= 60 => "良好",
        >= 40 => "一般",
        >= 20 => "较差",
        _ => "很差",
    };

    /// <summary>IP 类型的中文描述。按"越具体越优先"排列。</summary>
    public string IpTypeText
    {
        get
        {
            if (IsTor)
            {
                return "Tor 出口";
            }

            if (IsAbuser)
            {
                return "滥用来源";
            }

            if (IsDatacenter || (!IsResidential && !IsMobile))
            {
                return "机房 IP";
            }

            return IsMobile ? "移动网络" : "住宅 IP";
        }
    }

    /// <summary>风险标记的汇总，用于一行显示。没有风险项时返回空串。</summary>
    public string RiskText
    {
        get
        {
            var flags = new List<string>();
            if (IsDatacenter)
            {
                flags.Add("机房");
            }

            if (IsVpn)
            {
                flags.Add("VPN");
            }

            if (IsProxy)
            {
                flags.Add("代理");
            }

            if (IsTor)
            {
                flags.Add("Tor");
            }

            if (IsAbuser)
            {
                flags.Add("滥用");
            }

            return string.Join(" · ", flags);
        }
    }
}
