namespace ServiceLib.Services.Statistics;

public class StatisticsXrayService
{
    private const long linkBase = 1024;
    private ServerSpeedItem _serverSpeedItem = new();
    private readonly Config _config;
    private CancellationTokenSource? _cts;
    private readonly Func<ServerSpeedItem, Task>? _updateFunc;
    private string Url => $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort}/debug/vars";
    private static readonly string _tag = "StatisticsXrayService";

    public StatisticsXrayService(Config config, Func<ServerSpeedItem, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;

        Task.Run(Run);
    }

    public void Close()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private async Task Run()
    {
        Close();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            try
            {
                if (AppManager.Instance.RunningCoreType != ECoreType.Xray)
                {
                    continue;
                }

                var result = await HttpClientHelper.Instance.TryGetAsync(Url, token);
                if (result != null)
                {
                    var server = ParseOutput(result) ?? new ServerSpeedItem();
                    await _updateFunc!.Invoke(server);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(3000, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 从 /debug/vars 的一项里读出 uplink/downlink。
    ///
    /// outbound 在 DTO 里声明为 Hashtable，System.Text.Json 会把值反序列化成
    /// JsonElement 而不是字符串。上游原样 `value.ToString()` 再反序列化，
    /// 遇到 JsonElement 时取到的是原始 JSON 文本 —— 虽然偶尔能凑巧解析成功，
    /// 但键序或类型一变就静默变成 0。这里把三种可能形态显式处理掉。
    /// </summary>
    private static V2rayMetricsVarsLink? ReadLink(object? value)
    {
        switch (value)
        {
            case null:
                return null;

            case JsonElement el when el.ValueKind == JsonValueKind.Object:
                var up = el.TryGetProperty("uplink", out var u) && u.TryGetInt64(out var uv) ? uv : 0;
                var down = el.TryGetProperty("downlink", out var d) && d.TryGetInt64(out var dv) ? dv : 0;
                return new V2rayMetricsVarsLink { uplink = up, downlink = down };

            case JsonElement el when el.ValueKind == JsonValueKind.String:
                return JsonUtils.Deserialize<V2rayMetricsVarsLink>(el.GetString() ?? string.Empty);

            case string text when !string.IsNullOrWhiteSpace(text):
                return JsonUtils.Deserialize<V2rayMetricsVarsLink>(text);

            default:
                return null;
        }
    }

    private ServerSpeedItem? ParseOutput(string result)
    {
        try
        {
            var source = JsonUtils.Deserialize<V2rayMetricsVars>(result);
            if (source?.stats?.outbound == null)
            {
                return null;
            }

            ServerSpeedItem server = new();
            foreach (var key in source.stats.outbound.Keys.Cast<string>())
            {
                var state = ReadLink(source.stats.outbound[key]);
                if (state is null)
                {
                    continue;
                }

                if (key.StartsWith(Global.ProxyTag))
                {
                    server.ProxyUp += state.uplink / linkBase;
                    server.ProxyDown += state.downlink / linkBase;
                }
                else if (key == Global.DirectTag)
                {
                    server.DirectUp = state.uplink / linkBase;
                    server.DirectDown = state.downlink / linkBase;
                }
            }

            if (server.DirectDown < _serverSpeedItem.DirectDown || server.ProxyDown < _serverSpeedItem.ProxyDown)
            {
                _serverSpeedItem = new();
                return null;
            }

            ServerSpeedItem curItem = new()
            {
                ProxyUp = server.ProxyUp - _serverSpeedItem.ProxyUp,
                ProxyDown = server.ProxyDown - _serverSpeedItem.ProxyDown,
                DirectUp = server.DirectUp - _serverSpeedItem.DirectUp,
                DirectDown = server.DirectDown - _serverSpeedItem.DirectDown,
            };
            _serverSpeedItem = server;
            return curItem;
        }
        catch
        {
            // ignored
        }

        return null;
    }
}
