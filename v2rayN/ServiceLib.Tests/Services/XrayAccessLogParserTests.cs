namespace ServiceLib.Tests.Services;

/// <summary>
/// Xray access log 解析。用实测到的原文行做样本，注意箭头有两种写法（-&gt; 与 &gt;&gt;）。
/// </summary>
public class XrayAccessLogParserTests
{
    [Test]
    public async Task ParseHost_SingleArrow_ShouldExtractHost()
    {
        const string line = "2026/10/24 17:05:10.891071 from tcp:127.0.0.1:11494 accepted tcp:www.google.com:443 [socks -> proxy]";
        await XrayAccessLogParser.ParseHost(line).Should().BeEqualTo("www.google.com");
    }

    [Test]
    public async Task ParseHost_DoubleArrow_ShouldExtractHost()
    {
        const string line = "2026/10/24 17:05:12.323959 from tcp:127.0.0.1:11498 accepted tcp:speed.cloudflare.com:443 [socks >> proxy]";
        await XrayAccessLogParser.ParseHost(line).Should().BeEqualTo("speed.cloudflare.com");
    }

    [Test]
    public async Task ParseHost_WithoutFractionalSeconds_ShouldWork()
    {
        const string line = "2026/10/24 17:05:31 from tcp:127.0.0.1:11519 accepted tcp:www.gstatic.com:443 [socks -> proxy]";
        await XrayAccessLogParser.ParseHost(line).Should().BeEqualTo("www.gstatic.com");
    }

    [Test]
    public async Task ParseSourcePort_ShouldExtractPort()
    {
        const string line = "2026/10/24 17:05:10.891071 from tcp:127.0.0.1:11494 accepted tcp:www.google.com:443 [socks -> proxy]";
        await XrayAccessLogParser.ParseSourcePort(line).Should().BeEqualTo(11494);
    }

    [Test]
    public async Task ParseHost_NonConnectionLines_ShouldReturnNull()
    {
        await XrayAccessLogParser.ParseHost("Xray 26.3.27 (Xray, Penetrates Everything.)").Should().BeNull();
        await XrayAccessLogParser.ParseHost("2026/10/24 17:05:10 rejected tcp:1.2.3.4:443").Should().BeNull();
        await XrayAccessLogParser.ParseHost("").Should().BeNull();
        await XrayAccessLogParser.ParseHost(null).Should().BeNull();
    }

    [Test]
    public async Task ParseHost_UdpLine_ShouldExtractHost()
    {
        // UDP 的 DNS 查询也会进日志，按域名统计时同样算一次
        const string line = "2026/10/24 17:06:00.500000 from udp:127.0.0.1:5555 accepted udp:dns.google:53 [socks -> proxy]";
        await XrayAccessLogParser.ParseHost(line).Should().BeEqualTo("dns.google");
    }
}
