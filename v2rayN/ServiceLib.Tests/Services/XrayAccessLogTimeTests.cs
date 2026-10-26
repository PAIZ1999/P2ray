namespace ServiceLib.Tests.Services;

/// <summary>
/// access log 时间戳解析。
///
/// 用途：首次启用日志时，日志文件里可能已经有历史行；服务会把这些行按时间过滤掉，
/// 只统计本次启动之后的连接。解析失败必须返回 null（当作"不过滤"），不能抛。
/// </summary>
public class XrayAccessLogTimeTests
{
    [Test]
    public async Task ParseTime_WithMicroseconds_ShouldParse()
    {
        const string line = "2026/10/26 10:37:43.265778 from tcp:127.0.0.1:6979 accepted tcp:www.google.com:443 [socks -> proxy]";
        var t = XrayAccessLogParser.ParseTime(line);
        await (t is not null).Should().BeTrue();
        await t!.Value.ToString("yyyy-MM-dd HH:mm:ss").Should().BeEqualTo("2026-10-26 10:37:43");
    }

    [Test]
    public async Task ParseTime_WithMilliseconds_ShouldParse()
    {
        const string line = "2026/10/26 10:37:47.380 from tcp:127.0.0.1:6983 accepted tcp:www.gstatic.com:443 [socks -> proxy]";
        var t = XrayAccessLogParser.ParseTime(line);
        await (t is not null).Should().BeTrue();
        await t!.Value.ToString("HH:mm:ss.fff").Should().BeEqualTo("10:37:47.380");
    }

    [Test]
    public async Task ParseTime_WithoutFraction_ShouldParse()
    {
        const string line = "2026/10/26 10:37:48 from tcp:127.0.0.1:6985 accepted tcp:www.bing.com:443 [socks >> proxy]";
        var t = XrayAccessLogParser.ParseTime(line);
        await (t is not null).Should().BeTrue();
        await t!.Value.Minute.Should().BeEqualTo(37);
    }

    [Test]
    public async Task ParseTime_NonConnectionLine_ShouldReturnNull()
    {
        await XrayAccessLogParser.ParseTime("Xray 26.3.27 (Xray, Penetrates Everything.)").Should().BeNull();
        await XrayAccessLogParser.ParseTime("").Should().BeNull();
        await XrayAccessLogParser.ParseTime(null).Should().BeNull();
    }
}
