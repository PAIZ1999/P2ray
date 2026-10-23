namespace ServiceLib.Tests.Services;

/// <summary>
/// 统计保留策略。
///
/// 判定必须能区分三种 Key 形态，否则会误删或漏删：
///   时段表    "HourTick"（long，按小时）
///   域名表    "20261023|www.youtube.com"
///   应用表    "p|20261023|chrome"（带口径前缀）
/// </summary>
public class StatisticsRetentionTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 14, 0, 0);

    [Test]
    public async Task GetCutoffDay_Retention90_ShouldStartFrom91DaysAgo()
    {
        var cutoff = StatisticsRetentionHandler.GetCutoffDay(90, Now);
        await cutoff.Should().BeEqualTo("20260726");
    }

    [Test]
    public async Task GetCutoffDay_Zero_ShouldMeanKeepForever()
    {
        await StatisticsRetentionHandler.GetCutoffDay(0, Now).Should().BeNull();
        await StatisticsRetentionHandler.GetCutoffTick(0, Now).Should().BeNull();
    }

    [Test]
    public async Task IsExpired_DomainKey_ShouldCompareDatePart()
    {
        var cutoff = StatisticsRetentionHandler.GetCutoffDay(90, Now)!;
        await StatisticsRetentionHandler.IsExpired("20260101|www.google.com", cutoff).Should().BeTrue();
        await StatisticsRetentionHandler.IsExpired("20261023|www.google.com", cutoff).Should().BeFalse();
    }

    [Test]
    public async Task IsExpired_AppKeyWithModePrefix_ShouldStillFindDate()
    {
        var cutoff = StatisticsRetentionHandler.GetCutoffDay(90, Now)!;
        await StatisticsRetentionHandler.IsExpired("p|20260101|chrome", cutoff).Should().BeTrue();
        await StatisticsRetentionHandler.IsExpired("a|20261023|chrome", cutoff).Should().BeFalse();
    }

    [Test]
    public async Task IsExpired_KeyWithoutDate_ShouldNeverExpire()
    {
        // 不认识的 Key 形态一律保留：宁可多留，也不要误删用户数据
        var cutoff = StatisticsRetentionHandler.GetCutoffDay(90, Now)!;
        await StatisticsRetentionHandler.IsExpired("weird-key-without-date", cutoff).Should().BeFalse();
    }

    [Test]
    public async Task GetCutoffTick_ShouldMatchCutoffDay()
    {
        var tick = StatisticsRetentionHandler.GetCutoffTick(90, Now)!.Value;
        await new DateTime(tick).ToString("yyyyMMdd").Should().BeEqualTo(StatisticsRetentionHandler.GetCutoffDay(90, Now)!);
    }
}
