namespace ServiceLib.Tests.Services;

/// <summary>
/// 按应用统计的两个新组件：
///   - ClashConnectionsParser：从 /connections 里取 id / sourcePort / 字节数
///   - PortProcessResolver.ParseNetstatPorts：从 netstat 里取「本地端口 → PID」
///
/// 这两个解析决定了归因准不准，所以用真实返回格式钉住行为。
/// </summary>
public class AppStatComponentsTests
{
    [Test]
    public async Task ParseConnections_ShouldReadPortAndBytes()
    {
        const string json = """
        {
          "downloadTotal": 4096,
          "uploadTotal": 512,
          "connections": [
            {
              "id": "conn-1",
              "upload": 100,
              "download": 2048,
              "metadata": { "sourceIP": "127.0.0.1", "sourcePort": "52341", "host": "example.com" }
            }
          ]
        }
        """;

        var list = ClashConnectionsParser.Parse(json);
        await list.Count.Should().BeEqualTo(1);
        await list[0].Id.Should().BeEqualTo("conn-1");
        await list[0].SourcePort.Should().BeEqualTo(52341);
        await list[0].Up.Should().BeEqualTo(100L);
        await list[0].Down.Should().BeEqualTo(2048L);
    }

    [Test]
    public async Task ParseConnections_NoSourcePort_ShouldStillReturnEntry()
    {
        // 端口拿不到时仍保留连接（字节数还有用），只是归因不到进程
        const string json = """{"connections":[{"id":"c","upload":1,"download":2,"metadata":{}}]}""";
        var list = ClashConnectionsParser.Parse(json);
        await list.Count.Should().BeEqualTo(1);
        await list[0].SourcePort.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ParseConnections_Invalid_ShouldReturnEmpty()
    {
        await ClashConnectionsParser.Parse(null).Count.Should().BeEqualTo(0);
        await ClashConnectionsParser.Parse("not json").Count.Should().BeEqualTo(0);
        await ClashConnectionsParser.Parse("""{"connections":[]}""").Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ParseNetstatPorts_ShouldMapLocalPortToPid()
    {
        const string output = """
          Proto  Local Address          Foreign Address        State           PID
          TCP    127.0.0.1:52341        127.0.0.1:11808        ESTABLISHED     4242
          TCP    127.0.0.1:11808        0.0.0.0:0              LISTENING       1188
          TCP    192.168.1.5:50000      93.184.216.34:443      ESTABLISHED     9090
        """;

        var map = PortProcessResolver.ParseNetstatPorts(output);
        await map[52341].Should().BeEqualTo(4242);
        await map[11808].Should().BeEqualTo(1188);
        await map[50000].Should().BeEqualTo(9090);
    }

    [Test]
    public async Task ParseNetstatPorts_ShouldIgnoreUdpAndBadLines()
    {
        const string output = """
          UDP    0.0.0.0:5353           *:*                                    3312
          TCP    invalid-line
          TCP    127.0.0.1:1            127.0.0.1:2            ESTABLISHED     0
        """;

        var map = PortProcessResolver.ParseNetstatPorts(output);
        // UDP 不算；PID=0 的系统占位不算
        await map.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task TryParsePort_ShouldHandleIpv6AndGarbage()
    {
        await PortProcessResolver.TryParsePort("127.0.0.1:52341", out var p1).Should().BeTrue();
        await p1.Should().BeEqualTo(52341);

        await PortProcessResolver.TryParsePort("[::1]:6000", out var p2).Should().BeTrue();
        await p2.Should().BeEqualTo(6000);

        await PortProcessResolver.TryParsePort("no-port", out _).Should().BeFalse();
    }
}
