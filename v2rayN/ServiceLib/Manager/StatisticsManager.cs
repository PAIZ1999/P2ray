namespace ServiceLib.Manager;

public class StatisticsManager
{
    private static readonly Lazy<StatisticsManager> instance = new(() => new());
    public static StatisticsManager Instance => instance.Value;

    private Config _config;
    private ServerStatItem? _serverStatItem;
    private List<ServerStatItem> _lstServerStat;
    private Func<ServerSpeedItem, Task>? _updateFunc;

    private StatisticsXrayService? _statisticsXray;
    private StatisticsSingboxService? _statisticsSingbox;
    private static readonly string _tag = "StatisticsHandler";
    public List<ServerStatItem> ServerStat => _lstServerStat;

    public async Task Init(Config config, Func<ServerSpeedItem, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;
        if (config.GuiItem.EnableStatistics || _config.GuiItem.DisplayRealTimeSpeed)
        {
            // 先把三个轮询器起起来，再做数据库相关的初始化。
            //
            // 顺序很关键：v1.6.0~v1.10.0 里这几行原来在 LoadCurrentHourAsync /
            // PurgeExpired（同步阻塞版）之后，只要前面任何一步卡住或抛异常，
            // StartPolling 就永远不会执行 —— 表现是"内核明明在走流量，统计全是 0"。
            // 已用 --selftest 复现：内核走了 4,013,583 字节，数据库 0 增长。
            TrafficStatService.Instance.StartPolling();
            DomainStatService.Instance.StartPolling();
            XrayDomainStatService.Instance.Start();
            AppStatService.Instance.Start();

            await InitData();

            _statisticsXray = new StatisticsXrayService(config, UpdateServerStatHandler);
            _statisticsSingbox = new StatisticsSingboxService(config, UpdateServerStatHandler);

            // 把已经落库的当前小时累计读回内存，否则重启后本小时会从 0 覆盖重记。
            await TrafficStatService.Instance.LoadCurrentHourAsync();

            // 三张统计表只增不减，启动时按保留策略清一次过期数据（异步，不阻塞）
            await StatisticsRetentionHandler.PurgeExpiredAsync();
        }
    }

    public void Close()
    {
        try
        {
            _statisticsXray?.Close();
            _statisticsSingbox?.Close();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public async Task ClearAllServerStatistics()
    {
        await SQLiteHelper.Instance.ExecuteAsync($"delete from ServerStatItem ");
        _serverStatItem = null;
        _lstServerStat = [];
    }

    public async Task SaveTo()
    {
        try
        {
            // 退出前把时段统计的余量刷盘，否则最多丢掉一个刷盘间隔（20 秒）的流量。
            TrafficStatService.Instance.Flush();

            if (_lstServerStat != null)
            {
                await SQLiteHelper.Instance.UpdateAllAsync(_lstServerStat);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public async Task CloneServerStatItem(string indexId, string toIndexId)
    {
        if (_lstServerStat == null)
        {
            return;
        }

        if (indexId == toIndexId)
        {
            return;
        }

        var stat = _lstServerStat.FirstOrDefault(t => t.IndexId == indexId);
        if (stat == null)
        {
            return;
        }

        var toStat = JsonUtils.DeepCopy(stat);
        toStat.IndexId = toIndexId;
        await SQLiteHelper.Instance.ReplaceAsync(toStat);
        _lstServerStat.Add(toStat);
    }

    private async Task InitData()
    {
        await SQLiteHelper.Instance.ExecuteAsync($"delete from ServerStatItem where indexId not in ( select indexId from ProfileItem )");

        var ticks = DateTime.Now.Date.Ticks;
        await SQLiteHelper.Instance.ExecuteAsync($"update ServerStatItem set todayUp = 0,todayDown=0,dateNow={ticks} where dateNow<>{ticks}");

        _lstServerStat = await SQLiteHelper.Instance.TableAsync<ServerStatItem>().ToListAsync();
    }

    private async Task UpdateServerStatHandler(ServerSpeedItem server)
    {
        await UpdateServerStat(server);
    }

    private async Task UpdateServerStat(ServerSpeedItem server)
    {
        await GetServerStatItem(_config.IndexId);

        if (_serverStatItem is null)
        {
            return;
        }
        if (server.ProxyUp != 0 || server.ProxyDown != 0)
        {
            _serverStatItem.TodayUp += server.ProxyUp;
            _serverStatItem.TodayDown += server.ProxyDown;
            _serverStatItem.TotalUp += server.ProxyUp;
            _serverStatItem.TotalDown += server.ProxyDown;
        }

        server.IndexId = _config.IndexId;
        server.TodayUp = _serverStatItem.TodayUp;
        server.TodayDown = _serverStatItem.TodayDown;
        server.TotalUp = _serverStatItem.TotalUp;
        server.TotalDown = _serverStatItem.TotalDown;
        await _updateFunc?.Invoke(server);
    }

    private async Task GetServerStatItem(string indexId)
    {
        var ticks = DateTime.Now.Date.Ticks;
        if (_serverStatItem != null && _serverStatItem.IndexId != indexId)
        {
            _serverStatItem = null;
        }

        if (_serverStatItem == null)
        {
            _serverStatItem = _lstServerStat.FirstOrDefault(t => t.IndexId == indexId);
            if (_serverStatItem == null)
            {
                _serverStatItem = new ServerStatItem
                {
                    IndexId = indexId,
                    TotalUp = 0,
                    TotalDown = 0,
                    TodayUp = 0,
                    TodayDown = 0,
                    DateNow = ticks
                };
                await SQLiteHelper.Instance.ReplaceAsync(_serverStatItem);
                _lstServerStat.Add(_serverStatItem);
            }
        }

        if (_serverStatItem.DateNow != ticks)
        {
            _serverStatItem.TodayUp = 0;
            _serverStatItem.TodayDown = 0;
            _serverStatItem.DateNow = ticks;
        }
    }
}
