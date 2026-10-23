namespace ServiceLib.Tests.CoreConfig.Clash;

/// <summary>
/// mihomo 走的是「自定义配置」节点：节点地址是一个本地 YAML 文件。
/// 这条路径原本就能用，但发布包里一直没有 mihomo 内核，所以没人走过。
///
/// 这里盯三件事：
///   1. 能读取用户的 Clash YAML；
///   2. 会强制覆盖 mixed-port 与 external-controller —— 按域名统计依赖后者，
///      缺了它那份功能就是空的；
///   3. 用户自己的 proxies 段被保留，不被改写。
/// </summary>
[NotInParallel]
public class CoreConfigClashServiceTests
{
    private const string SampleYaml = """
        proxies:
        - name: reality-yy
          type: vless
          server: example.com
          port: 443
          uuid: 11111111-2222-3333-4444-555555555555
          network: tcp
          tls: true
          udp: true
          flow: xtls-rprx-vision
          servername: amd.com
          client-fingerprint: chrome
          reality-opts:
            public-key: jNXHt1yRo0vDuchQlIP6Z0ZvjT3KtzVI-T4E7RoLJS0
            short-id: 8ebe
        proxy-groups:
        - name: PROXY
          type: select
          proxies:
          - reality-yy
          - DIRECT
        rules:
        - MATCH,PROXY
        """;

    [Test]
    public async Task GenerateClientCustomConfig_ShouldInjectPortsAndKeepProxies()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var yamlPath = Path.Combine(Path.GetTempPath(), $"p2ray-clash-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(yamlPath, SampleYaml);

        // 自定义配置节点：Address 就是配置文件路径
        var node = new ProfileItem
        {
            IndexId = "custom-mihomo-1",
            Remarks = "clash-test",
            ConfigType = EConfigType.Custom,
            CoreType = ECoreType.mihomo,
            Address = yamlPath,
        };

        var fileName = $"clash-out-{Guid.NewGuid():N}.yaml";
        var outPath = Utils.GetConfigPath(fileName);

        try
        {
            var result = await new CoreConfigClashService(config, false).GenerateClientCustomConfig(node, outPath);

            await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");

            var content = await File.ReadAllTextAsync(outPath);

            // 端口由程序覆盖：按域名统计与本地代理都靠它们
            await content.Should().Contain($"external-controller: {Global.Loopback}:{AppManager.Instance.StatePort2}");
            await content.Should().Contain($"mixed-port: {AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");

            // 用户的节点定义必须原样保留
            await content.Should().Contain("reality-yy");
            await content.Should().Contain("reality-opts");
            await content.Should().Contain("MATCH,PROXY");
        }
        finally
        {
            File.Delete(yamlPath);
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }
        }
    }
}
