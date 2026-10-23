namespace ServiceLib.Tests.Services;

/// <summary>
/// 内核版本解析。
///
/// 三个内核的 -version 输出格式完全不同，不能按固定位置取：
/// xray 第一段就是版本，sing-box 前面有 "version"，mihomo 还带 "Meta v"。
/// 这里用真实输出做样本钉住行为，顺便钉住"取不到时返回 null"。
/// </summary>
public class CoreVersionServiceTests
{
    [Test]
    public async Task ParseVersion_Xray_ShouldExtractSemver()
    {
        const string output = "Xray 26.3.27 (Xray, Penetrates Everything.) Custom (go1.24.1 windows/amd64)";
        await CoreVersionService.ParseVersion(output).Should().BeEqualTo("26.3.27");
    }

    [Test]
    public async Task ParseVersion_Singbox_ShouldExtractSemver()
    {
        const string output = "sing-box version 1.14.3";
        await CoreVersionService.ParseVersion(output).Should().BeEqualTo("1.14.3");
    }

    [Test]
    public async Task ParseVersion_Mihomo_ShouldStripLeadingV()
    {
        const string output = "Mihomo Meta v1.19.33 windows amd64 with go1.26.10 Wed Oct 21 16:44:15 UTC 2026";
        await CoreVersionService.ParseVersion(output).Should().BeEqualTo("1.19.33");
    }

    [Test]
    public async Task ParseVersion_ShouldIgnoreDatesAfterFirstLines()
    {
        // 构建信息里的日期（2026-10-21）不能被当成版本号
        const string output = "sing-box version 1.14.3\nbuild: 2026-10-21T16:44:15Z";
        await CoreVersionService.ParseVersion(output).Should().BeEqualTo("1.14.3");
    }

    [Test]
    public async Task ParseVersion_EmptyOrGarbage_ShouldReturnNull()
    {
        await CoreVersionService.ParseVersion(null).Should().BeNull();
        await CoreVersionService.ParseVersion("").Should().BeNull();
        await CoreVersionService.ParseVersion("command not found").Should().BeNull();
    }
}
