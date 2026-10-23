namespace ServiceLib.Handler;

/// <summary>
/// 统计数据的保留策略。
///
/// 三张表（时段 / 域名 / 应用）都是"只写不删"，长期运行会让数据库持续膨胀。
/// 这里统一按天数清理：保留策略为 0 时表示永久保留，不做任何删除。
/// </summary>
public static class StatisticsRetentionHandler
{
    private static readonly string _tag = "StatisticsRetention";

    /// <summary>当前应保留的最早日期（yyyyMMdd）；返回 null 表示永久保留。</summary>
    public static string? GetCutoffDay(int retentionDays, DateTime now)
    {
        if (retentionDays <= 0)
        {
            return null;
        }

        return now.Date.AddDays(-(retentionDays - 1)).ToString("yyyyMMdd");
    }

    /// <summary>当前应保留的最早小时（Tick）；返回 null 表示永久保留。</summary>
    public static long? GetCutoffTick(int retentionDays, DateTime now)
    {
        if (retentionDays <= 0)
        {
            return null;
        }

        var cutoff = now.Date.AddDays(-(retentionDays - 1));
        return cutoff.Ticks;
    }

    /// <summary>
    /// 清理三张统计表里过期的行。启动时调用一次即可。
    ///
    /// 必须是异步的：这个方法以前用 .GetAwaiter().GetResult() 同步等数据库，
    /// 而它被 StatisticsManager.Init 在 StartPolling 之前调用 —— 一旦阻塞，
    /// 后面的三个 StartPolling 全都不执行，流量统计整体失效。
    /// </summary>
    public static async Task PurgeExpiredAsync()
    {
        try
        {
            var days = AppManager.Instance.Config.GuiItem.TrafficRetentionDays;
            var now = DateTime.Now;

            var cutoffDay = GetCutoffDay(days, now);
            if (cutoffDay is null)
            {
                return;
            }

            var cutoffTick = GetCutoffTick(days, now)!.Value;

            var removed = 0;
            removed += await SQLiteHelper.Instance.ExecuteAsync($"delete from TrafficStatItem where HourTick < {cutoffTick}");

            // 域名与应用表的 Key 是 "yyyyMMdd|名称"（应用表还带模式前缀 "p|" / "a|"）
            // 这里用 LIKE 前缀匹配会误伤，改为取出主键在内存里判断，行数本来就有限。
            var domainRows = await SQLiteHelper.Instance.TableAsync<DomainStatItem>().ToListAsync();
            removed += await PurgeDayKeyedAsync("DomainStatItem", domainRows.Select(t => t.Key).ToList(), cutoffDay);

            var appRows = await SQLiteHelper.Instance.TableAsync<AppStatItem>().ToListAsync();
            removed += await PurgeDayKeyedAsync("AppStatItem", appRows.Select(t => t.Key).ToList(), cutoffDay);

            if (removed > 0)
            {
                Logging.SaveLog($"保留 {days} 天，清理过期统计 {removed} 行");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.PurgeExpired", ex);
        }
    }

    private static async Task<int> PurgeDayKeyedAsync(string table, List<string> keys, string cutoffDay)
    {
        var removed = 0;
        foreach (var key in keys.Where(k => !k.IsNullOrEmpty() && IsExpired(k, cutoffDay)))
        {
            // 表名与 Key 都由本文件生成，Key 只做单引号转义，不拼接外部输入
            var safeKey = key.Replace("'", "''");
            removed += await SQLiteHelper.Instance.ExecuteAsync($"delete from {table} where Key = '{safeKey}'");
        }

        return removed;
    }
    /// <summary>
    /// Key 形如 "20261023|host" 或 "p|20261023|chrome"。
    /// 取其中的日期段与保留期限比较。
    /// </summary>
    public static bool IsExpired(string key, string cutoffDay)
    {
        var parts = key.Split('|');
        var day = parts.FirstOrDefault(p => p.Length == 8 && p.All(char.IsAsciiDigit));
        if (day is null)
        {
            return false;
        }

        return string.CompareOrdinal(day, cutoffDay) < 0;
    }
}
