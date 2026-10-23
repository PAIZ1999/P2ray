namespace ServiceLib.Tests.Services;

/// <summary>
/// 概览数据的两段纯逻辑。
///
/// 背景：时段统计原本只读 xray 的 /debug/vars，切到 sing-box 后概览恒为 0。
/// 现在按内核分流，sing-box / mihomo 读 clash API 的 uploadTotal / downloadTotal。
/// 这里覆盖解析与差值两段，避免再出现"两条路只有一条能用"。
/// </summary>
public class TrafficTotalsTests
{
    [Test]
    public async Task ParseClashTotals_SingboxFormat_ShouldReadBothTotals()
    {
        // 实测自 sing-box 1.14.3 的 /connections 返回
        const string json = """{"downloadTotal":9335864,"uploadTotal":24815,"connections":[],"memory":0}""";
        var parsed = TrafficTotalsParser.ParseClashTotals(json);
        await parsed.HasValue.Should().BeTrue();
        await parsed!.Value.Up.Should().BeEqualTo(24815L);
        await parsed.Value.Down.Should().BeEqualTo(9335864L);
    }

    [Test]
    public async Task ParseClashTotals_StringNumbers_ShouldStillWork()
    {
        const string json = """{"uploadTotal":"1234","downloadTotal":"5678"}""";
        var parsed = TrafficTotalsParser.ParseClashTotals(json);
        await parsed!.Value.Up.Should().BeEqualTo(1234L);
        await parsed.Value.Down.Should().BeEqualTo(5678L);
    }

    [Test]
    public async Task ParseClashTotals_Invalid_ShouldReturnNull()
    {
        await TrafficTotalsParser.ParseClashTotals(null).Should().BeNull();
        await TrafficTotalsParser.ParseClashTotals("").Should().BeNull();
        await TrafficTotalsParser.ParseClashTotals("404 page not found").Should().BeNull();
        await TrafficTotalsParser.ParseClashTotals("""{"connections":[]}""").Should().BeNull();
    }

    [Test]
    public async Task Tracker_FirstReading_ShouldOnlySetBaseline()
    {
        var tracker = new TrafficDeltaTracker();
        var delta = tracker.Feed(1000, 2000);
        await delta.Up.Should().BeEqualTo(0L);
        await delta.Down.Should().BeEqualTo(0L);
    }

    [Test]
    public async Task Tracker_SubsequentReadings_ShouldReturnDelta()
    {
        var tracker = new TrafficDeltaTracker();
        tracker.Feed(1000, 2000);
        var delta = tracker.Feed(1500, 5000);
        await delta.Up.Should().BeEqualTo(500L);
        await delta.Down.Should().BeEqualTo(3000L);
    }

    [Test]
    public async Task Tracker_CounterReset_ShouldNotProduceNegative()
    {
        // 内核重启：计数从 0 重新开始，此时只能重置基线
        var tracker = new TrafficDeltaTracker();
        tracker.Feed(10_000, 20_000);
        var delta = tracker.Feed(100, 200);
        await delta.Up.Should().BeEqualTo(0L);
        await delta.Down.Should().BeEqualTo(0L);

        // 重置之后继续正常累加
        var next = tracker.Feed(300, 500);
        await next.Up.Should().BeEqualTo(200L);
        await next.Down.Should().BeEqualTo(300L);
    }

    [Test]
    public async Task Tracker_OneSideReset_ShouldAlsoReset()
    {
        var tracker = new TrafficDeltaTracker();
        tracker.Feed(1000, 1000);
        var delta = tracker.Feed(1200, 500);
        await delta.Up.Should().BeEqualTo(0L);
        await delta.Down.Should().BeEqualTo(0L);
    }
}
