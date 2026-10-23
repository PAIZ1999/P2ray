namespace ServiceLib.Tests.Handler;

/// <summary>
/// Clash 订阅识别。
///
/// 判断必须"够准"但不能太严：太松会把 base64 节点列表当成 YAML 去落盘，
/// 太严会漏掉写了 proxy-providers 而不是 proxy-groups 的订阅。
/// </summary>
public class ClashSubscriptionHandlerTests
{
    [Test]
    public async Task LooksLikeClashYaml_TypicalConfig_ShouldBeTrue()
    {
        const string yaml = """
            proxies:
            - name: node-1
              type: vless
              server: example.com
            proxy-groups:
            - name: PROXY
              type: select
            rules:
            - MATCH,PROXY
            """;

        await ClashSubscriptionHandler.LooksLikeClashYaml(yaml).Should().BeTrue();
    }

    [Test]
    public async Task LooksLikeClashYaml_ProviderOnlyVariant_ShouldBeTrue()
    {
        // 只写 proxy-providers、没有 proxy-groups 的订阅也存在
        const string yaml = """
            proxies: []
            proxy-providers:
              sub:
                type: http
                url: https://example.com/sub
            rules:
            - MATCH,DIRECT
            """;

        await ClashSubscriptionHandler.LooksLikeClashYaml(yaml).Should().BeTrue();
    }

    [Test]
    public async Task LooksLikeClashYaml_Base64NodeList_ShouldBeFalse()
    {
        const string list = "dm1lc3M6Ly95d3p4a3Q2OS0xLnBpZmxvdy54eXo6MjA5Ng==";
        await ClashSubscriptionHandler.LooksLikeClashYaml(list).Should().BeFalse();
    }

    [Test]
    public async Task LooksLikeClashYaml_JsonConfig_ShouldBeFalse()
    {
        const string json = """{"outbounds":[{"type":"vless"}]}""";
        await ClashSubscriptionHandler.LooksLikeClashYaml(json).Should().BeFalse();
    }

    [Test]
    public async Task LooksLikeClashYaml_ProxiesWithoutGroupsOrRules_ShouldBeFalse()
    {
        // 只有 proxies 一条、没有分组也没有规则：多半不是完整配置
        const string yaml = """
            proxies:
            - name: single
              type: socks5
            """;

        await ClashSubscriptionHandler.LooksLikeClashYaml(yaml).Should().BeFalse();
    }

    [Test]
    public async Task LooksLikeClashYaml_NullOrEmpty_ShouldBeFalse()
    {
        await ClashSubscriptionHandler.LooksLikeClashYaml(null).Should().BeFalse();
        await ClashSubscriptionHandler.LooksLikeClashYaml("").Should().BeFalse();
    }
}
