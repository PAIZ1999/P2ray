namespace ServiceLib.Services;

/// <summary>
/// 网络位置探测：出口 IP、归属地，以及常用站点的连通性。
///
/// 设计要点：
/// 1. 全部请求走本地 socks 入站 —— 用户关心的是"经过代理之后，世界看到我是谁"，
///    直连探测出来的 IP 和延迟没有参考价值。
/// 2. 连通性用极小的探测目标（generate_204 / favicon / cdn-cgi/trace），
///    只取响应头即可判定通断，不下载正文。
/// 3. 串行探测且每个目标独立超时：并发打十几个站点在弱线路下会互相拖慢，
///    测出来的延迟也不准。
/// 4. IP 与归属地分两步：先用 ipify 拿"我是谁"，再用 ip.net.coffee 的 geoip
///    查这个 IP 的省/市/运营商。geoip 接口要求显式 IP，不能直接查自己。
/// </summary>
public class NetProbeService
{
    private static readonly string _tag = "NetProbeService";

    /// <summary>
    /// 本轮探测走不走代理，在构造时定一次，整轮不再变。
    ///
    /// 为什么必须钉死：首页先查出口 IP、再逐个测站点，两步之间有几百毫秒到几秒。
    /// 如果每一步都现读 RunningCoreType，用户在探测途中点了"连接"，就会出现
    /// IP 走直连拿到本地归属地、站点却走代理测出全部可用的组合 ——
    /// 界面上一半是国内 IP、一半是境外全通，自相矛盾。
    /// 一轮探测的所有请求必须走同一条路径，结果才有可比性。
    /// </summary>
    private readonly bool _useProxy;
    private readonly int _port;
    private readonly HttpClient _client;

    /// <summary>
    /// 固定直连的客户端，用于查"不经过代理时，外面看到我是谁"。
    ///
    /// 为什么不复用 _client：_client 跟随本轮路径，走代理时它测出来的还是同一个
    /// 境外出口 IP，国内出口就永远查不到。两条数据必须用两个不同路径的 client
    /// 才可能同时拿到。
    /// </summary>
    private readonly HttpClient _directClient;

    public NetProbeService()
    {
        _useProxy = IsCoreRunning();
        _port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        _client = CreateClient(_port, _useProxy);
        _directClient = CreateClient(_port, false);
    }

    /// <summary>
    /// 查询自身出口 IP 的候选源，按顺序尝试直到拿到合法 IP。
    ///
    /// 为什么要多个：实测直连时 api.ipify.org 会被 RST（WinError 10054），
    /// 只留一个源会让"未连接"状态下永远显示"获取失败"。
    /// </summary>
    private static readonly string[] SelfIpUrls =
    [
        "https://ipinfo.io/json",
        "https://api.ip.sb/geoip",
        "https://api.ipify.org?format=json",
        "http://ip-api.com/json/?fields=status,query",
    ];

    /// <summary>
    /// 一次拿全 IP + 归属地（含中文）。选它作为首选是因为：
    /// - 单次请求就返回 country/regionName/city/isp，省掉"先查 IP 再查归属地"的第二跳；
    /// - 支持 lang=zh-CN，省/市直接是中文，不用自己维护映射表；
    /// - 实测直连 492ms，而 ip.net.coffee 的 geoip 要 6.6s。
    /// </summary>
    private const string IpApiUrl =
        "http://ip-api.com/json/?fields=status,message,country,countryCode,regionName,city,isp,query&lang=zh-CN";

    /// <summary>备选归属地源：ip-api 限流（45 次/分钟）时用它。</summary>
    private const string GeoIpUrl = "https://ip.net.coffee/api/geoip/";

    /// <summary>
    /// IP 风险评分接口，后面拼 IP。
    /// 注意不是 /api/ip/score 也不是 /api/ipv2/ —— 那两个都返回 400 Invalid path。
    /// </summary>
    private const string IpLookupUrl = "https://ip.net.coffee/api/ip/lookup/";

    /// <summary>探测目标的定义。Slug 用于取本地图标素材，Tag 用于界面分组。</summary>
    private sealed record ProbeTarget(string Name, string Url, string Tag, string Slug);

    /// <summary>
    /// 目标清单。分三组，各自回答一个不同的问题：
    /// - 国际：最基本的"能不能出去"；
    /// - AI：这些站单独风控，通外网不等于能上；
    /// - 国内：作对照，也能看出分流规则是否生效。
    ///
    /// 探测 URL 取自 ip.net.coffee/link/ 的实测清单 —— 都是极小的 favicon 或
    /// 204，只取响应头，不下载正文，弱线路下也不会拖满超时。
    ///
    /// 踩过的坑：
    /// - GitHub 的 /generate_204 不存在（404），必须用 api.github.com/zen；
    /// - ChatGPT / Claude 的 favicon 对非浏览器指纹返回 403，改用 cdn-cgi/trace；
    /// - Gemini 的 gemini.google.com/favicon.ico 返回 404，而 gstatic 上的 favicon
    ///   直连就能 200 —— 拿它探测等于没测。改用 /app 页面本身：直连超时、走代理 200，
    ///   这才是"能不能用 Gemini"的真实答案。
    ///   代价是会拉到 796KB 的正文，但探测只看响应头就断开（ResponseHeadersRead），
    ///   正文不会下载完，实测 1.4s 出结果。
    /// - Gemini 的 generate_204 常超时，也不能拿它当探针。
    /// </summary>
    private static readonly ProbeTarget[] Targets =
    [
        // 国际
        new("Google", "https://www.google.com/generate_204", "国际", "google"),
        new("YouTube", "https://www.youtube.com/generate_204", "国际", "youtube"),
        new("GitHub", "https://api.github.com/zen", "国际", "github"),
        new("Cloudflare", "https://1.1.1.1/cdn-cgi/trace", "国际", "cloudflare"),

        // AI —— 单独风控，通外网不代表能上
        new("ChatGPT", "https://chatgpt.com/cdn-cgi/trace", "AI", "openai"),
        new("Claude", "https://claude.ai/cdn-cgi/trace", "AI", "claude"),
        new("Gemini", "https://gemini.google.com/app", "AI", "gemini"),

        // 国内 —— 作对照，也能看出分流规则是否生效
        new("百度", "https://www.baidu.com/favicon.ico", "国内", "baidu"),
        new("淘宝", "https://www.taobao.com/favicon.ico", "国内", "taobao"),
        new("微信", "https://res.wx.qq.com/a/wx_fed/assets/res/NTI4MWU5.ico", "国内", "weixin"),
    ];
    /// <summary>单个目标的探测超时。太长会让整轮探测拖到十几秒。</summary>
    private static readonly TimeSpan PerTargetTimeout = TimeSpan.FromSeconds(6);

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>
    /// 探测目标的占位行，供界面在出结果前就把列表排好。
    /// 带上 Slug 和 Tag，这样探测途中也能看到图标与分组，
    /// 而不是先渲染一排无名灰框、出结果时再整体跳动一次。
    /// </summary>
    public static IReadOnlyList<NetProbeResult> CreatePlaceholders()
        => Targets.Select(t => new NetProbeResult { Name = t.Name, Tag = t.Tag, Slug = t.Slug }).ToList();

    /// <summary>
    /// 本轮探测是否走了代理。界面需要据此把结果说清楚：
    /// 未连接时的"境外站点全部可用"是直连测出来的，和连上代理后的可用性不是一回事，
    /// 不加区分地显示"N / M 可用"会让用户以为代理已经在工作。
    /// </summary>
    public bool UsedProxy => _useProxy;

    /// <summary>
    /// 探测出口 IP 与归属地。任一步失败返回 null，由调用方决定显示占位符还是保留旧值。
    /// 路径跟随本轮：走代理时这条就是"境外出口"，直连时就是"国内出口"。
    /// </summary>
    public async Task<IpLocationResult?> GetIpLocationAsync(CancellationToken cancellationToken = default)
    {
        return await GetIpLocationCoreAsync(_client, cancellationToken);
    }

    /// <summary>
    /// 查不经过代理时的出口 IP —— 也就是用户本地宽带对外的地址。
    ///
    /// 即便当前在代理模式，这条也要能查出来：用户需要同时看到
    /// "我的真实出口" 和 "代理把我伪装成的出口"，两者一对比就知道代理是否在起作用。
    /// 所以这里固定用 _directClient，不走 _client。
    /// </summary>
    public async Task<IpLocationResult?> GetDomesticIpAsync(CancellationToken cancellationToken = default)
    {
        // 已经在直连状态时两条路径相同，不必再发一遍请求
        if (!_useProxy)
        {
            return null;
        }

        return await GetIpLocationCoreAsync(_directClient, cancellationToken);
    }

    private static async Task<IpLocationResult?> GetIpLocationCoreAsync(HttpClient client, CancellationToken cancellationToken)
    {
        // 首选：一次请求拿到 IP + 中文归属地
        var quick = await GetFromIpApiAsync(client, cancellationToken);
        if (quick is not null)
        {
            return quick;
        }

        // 回退：先查 IP，再用 ip.net.coffee 查归属地
        var ip = await GetSelfIpAsync(client, cancellationToken);
        if (ip.IsNullOrEmpty())
        {
            return null;
        }

        var location = await GetGeoAsync(client, ip, cancellationToken);
        location.Ip = ip;
        return location;
    }

    /// <summary>ip-api 一次返回全部字段。status 不是 success 视为失败（含限流）。</summary>
    private static async Task<IpLocationResult?> GetFromIpApiAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var raw = await RequestAsync(client, IpApiUrl, cancellationToken);
        if (raw.IsNullOrEmpty())
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (GetString(root, "status") != "success")
            {
                return null;
            }

            var ip = GetString(root, "query");
            if (ip.IsNullOrEmpty() || !IPAddress.TryParse(ip, out _))
            {
                return null;
            }

            return new IpLocationResult
            {
                Ip = ip,
                Country = GetString(root, "country"),
                CountryCode = GetString(root, "countryCode"),
                Region = GetString(root, "regionName"),
                City = GetString(root, "city"),
                Isp = GetString(root, "isp"),
                // 这个源的中文名已经是中文，标记一下避免被中文映射表覆盖成英文
                AlreadyLocalized = true,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 查这个 IP 的风险评分。
    ///
    /// 接口是 /api/ip/lookup/{ip}，返回一份很大的 JSON（约 3.5 KB，含地理多源比对、
    /// ASN 历史、同段邻居等）。这里只取界面要用的十来个字段。
    /// 失败不影响连通性面板，评分是可选的附加信息。
    /// </summary>
    public async Task<IpScoreResult?> GetIpScoreAsync(string ip, CancellationToken cancellationToken = default)
    {
        if (ip.IsNullOrEmpty() || !IPAddress.TryParse(ip, out _))
        {
            return null;
        }

        var raw = await RequestAsync(_client, $"{IpLookupUrl}{Uri.EscapeDataString(ip)}", cancellationToken);
        if (raw.IsNullOrEmpty())
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // 接口对非法 IP 返回的仍是 200 + 错误体，用 ip 字段是否回显来判定
            if (GetString(root, "ip").IsNullOrEmpty())
            {
                return null;
            }

            var result = new IpScoreResult
            {
                TrustScore = GetInt(root, "trust_score"),
                IsDatacenter = GetBool(root, "is_datacenter"),
                IsResidential = GetBool(root, "isResidential"),
                IsVpn = GetBool(root, "is_vpn"),
                IsProxy = GetBool(root, "is_proxy"),
                IsTor = GetBool(root, "is_tor"),
                IsAbuser = GetBool(root, "is_abuser"),
                IsMobile = GetBool(root, "is_mobile"),
                Asn = GetInt(root, "asn") is var asn && asn > 0 ? $"AS{asn}" : null,
                AsOrganization = GetString(root, "asOrganization"),
                CompanyName = GetString(root, "company_name"),
                DatacenterName = GetString(root, "datacenter_name"),
                Rdns = GetString(root, "rdns"),
            };

            if (root.TryGetProperty("ai_verdict", out var verdict) && verdict.ValueKind == JsonValueKind.Object)
            {
                result.AiVerdictLabel = GetString(verdict, "label");
                result.AiVerdictConfidence = GetInt(verdict, "confidence");
                result.AiVerdictReason = GetString(verdict, "reasoning");
            }

            return result;
        }
        catch (JsonException ex)
        {
            Logging.SaveLog($"{_tag}.GetIpScoreAsync", ex);
            return null;
        }
    }

    private static int GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v))
        {
            return 0;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : 0,
            // abuser_score 之类是 "0.0381 (High)" 这种混合串，取不出整数就当 0
            JsonValueKind.String => int.TryParse(v.GetString(), out var parsed) ? parsed : 0,
            _ => 0,
        };
    }

    private static bool GetBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static async Task<string?> GetSelfIpAsync(HttpClient client, CancellationToken cancellationToken)
    {
        foreach (var url in SelfIpUrls)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var raw = await RequestAsync(client, url, cancellationToken);
            if (raw.IsNullOrEmpty())
            {
                continue;
            }

            var ip = ParseIp(raw);
            if (ip is not null)
            {
                return ip;
            }
        }

        return null;
    }

    /// <summary>从各家的 JSON 里挑出 IP。字段名不统一，逐个试。</summary>
    private static string? ParseIp(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            foreach (var field in (string[])["ip", "query", "clientIp", "ip_addr"])
            {
                var value = GetString(root, field);
                // 只接受合法 IP：错误页 / 限流提示不能被当成 IP 显示出来
                if (value is not null && IPAddress.TryParse(value, out _))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
            // 忽略，换下一个源
        }

        return null;
    }

    private static async Task<IpLocationResult> GetGeoAsync(HttpClient client, string ip, CancellationToken cancellationToken)
    {
        var result = new IpLocationResult { Ip = ip };

        var raw = await RequestAsync(client, $"{GeoIpUrl}{Uri.EscapeDataString(ip)}", cancellationToken);
        if (raw.IsNullOrEmpty())
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            result.Country = GetString(root, "country");
            result.CountryCode = GetString(root, "country_code");
            result.Region = GetString(root, "region");
            result.City = GetString(root, "city");
            result.Isp = GetString(root, "isp");
        }
        catch (JsonException ex)
        {
            Logging.SaveLog($"{_tag}.GetGeoAsync", ex);
        }

        return result;
    }

    /// <summary>
    /// 逐个探测各站点。返回顺序与目标清单一致，方便界面稳定排列。
    /// </summary>
    public async Task<List<NetProbeResult>> ProbeAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<NetProbeResult>(Targets.Length);

        foreach (var target in Targets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            results.Add(await ProbeOneAsync(target, cancellationToken));
        }

        return results;
    }

    private async Task<NetProbeResult> ProbeOneAsync(ProbeTarget target, CancellationToken cancellationToken)
    {
        var result = new NetProbeResult { Name = target.Name, Tag = target.Tag, Slug = target.Slug };
        var timer = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PerTargetTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            using var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            timer.Stop();
            result.Ok = response.IsSuccessStatusCode;
            result.LatencyMs = (int)timer.Elapsed.TotalMilliseconds;

            if (!result.Ok)
            {
                result.Error = $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            timer.Stop();
            result.Ok = false;
            result.Error = "超时";
        }
        catch (Exception ex)
        {
            timer.Stop();
            result.Ok = false;
            result.Error = ex.GetBaseException().Message;
        }

        // 走到这里说明这个目标确实测过了，无论成功失败都要置位。
        // 界面靠它把"还没轮到"（灰 —）和"测过但失败"（红 失败）区分开。
        result.HasResult = true;
        return result;
    }

    private static async Task<string?> RequestAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await client.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.RequestAsync", ex);
            return null;
        }
    }

    /// <summary>
    /// 建一个绑定到指定路径（代理或直连）的 HttpClient。
    ///
    /// 不跨实例复用：一轮探测会发十几个请求，复用同一个 client 已经够省；
    /// 而跨轮复用要判断端口和路径是否变化，判断错一次就会让整轮探测
    /// 走上一条和预期相反的路径。这里宁可每轮新建一个。
    ///
    /// 关键：必须显式给 Proxy 赋值，不能只设 UseProxy=false。
    /// SocketsHttpHandler 的默认 Proxy 是 HttpClient.DefaultProxy，
    /// 它会去读系统 WinINET / 环境变量里的代理配置 ——
    /// 机器上装了别的 VPN 客户端时，那套配置会把"直连"探测
    /// 悄悄导到别处，测出来的延迟和连通性都是假的。
    /// 直连场景显式赋 null 才是真的不走代理。
    /// </summary>
    private static HttpClient CreateClient(int port, bool useProxy)
    {
        var handler = new SocketsHttpHandler
        {
            Proxy = useProxy ? new WebProxy($"socks5://{Global.Loopback}:{port}") : null,
            UseProxy = useProxy,
            ConnectTimeout = PerTargetTimeout,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// 内核是否在运行。
    ///
    /// 两个判据缺一不可：
    /// 1. RunningCoreType —— 内核类型。它由 CoreManager 在进程确认存活后写入。
    /// 2. 本地 socks 端口能否连上 —— 首屏探测会早于内核就绪（启动后 600ms 就开始），
    ///    那时 RunningCoreType 还是默认值，只看它会把"走代理"误判成"直连"，
    ///    于是面板显示真实公网 IP、标上"（直连）"，而实际上内核已经在工作了。
    ///
    /// 反过来单独看端口也不行：端口可能被别的程序占着，
    /// 那正是之前"境外站点全部可用"假数据的原因。两个条件同时成立才认定走代理。
    /// </summary>
    private static bool IsCoreRunning()
    {
        try
        {
            var running = AppManager.Instance.RunningCoreType;
            var coreTypeOk = running is ECoreType.Xray or ECoreType.v2fly or ECoreType.v2fly_v5
                or ECoreType.sing_box or ECoreType.mihomo;
            if (!coreTypeOk)
            {
                return false;
            }

            return IsPortOpen(AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>本地端口是否真的有人在监听。50ms 超时足够判定回环连接。</summary>
    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(Global.Loopback, port);
            if (!task.Wait(TimeSpan.FromMilliseconds(50)))
            {
                return false;
            }

            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>把探测结果拼成一句归属地描述，如「中国 浙江 宁波 · 中国移动」。</summary>
    public static string FormatLocation(IpLocationResult? info)
    {
        if (info is null)
        {
            return string.Empty;
        }

        // 国家名走中文映射，省/市数据源只给英文原文，保留原样。
        var geo = string.Join(" ", new[] { info.CountryDisplay, info.Region, info.City }
            .Where(s => s.IsNotEmpty())
            .Distinct());

        // 注意不能用 IsEmpty()：项目里引了 ReactiveUI，
        // IsEmpty() 会被解析成 IObservable 的扩展方法，编译不过。
        return string.IsNullOrEmpty(info.Isp) ? geo : $"{geo} · {info.Isp}";
    }
}
