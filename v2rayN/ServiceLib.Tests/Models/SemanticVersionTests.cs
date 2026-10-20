namespace ServiceLib.Tests.Models;

/// <summary>
/// SemanticVersion.TryParse 的回归测试。
///
/// 起因：构造函数内部 catch 后回退成 0.0.0，导致 TryParse 恒为 true。
/// 更新检查拿到 GitHub 的 "…/releases"（仓库还没有 Release 时
/// /releases/latest 会重定向到这里）就把它当版本号 0.0.0，误报"有新版本"。
/// </summary>
public class SemanticVersionTests
{
    [Test]
    public async Task TryParse_ShouldRejectNonVersionStrings()
    {
        // 这些串会被构造函数"解析成功"成 0.0.0，必须在这一层拦住
        var invalid = new[] { null, "", "  ", "releases", "latest", "v", "abc", "1.2.x", "1..2", "1.2.3.4.5" };

        foreach (var input in invalid)
        {
            var ok = SemanticVersion.TryParse(input, out var parsed);
            await ok.Should().BeFalse();
            await parsed.Should().BeNull();
        }
    }

    [Test]
    public async Task TryParse_ShouldPreserveRawString()
    {
        var cases = new[] { "1.2", "1.2.3", "v1.2.3", "V1.2.3", "v26.3.27", "v1.2.3-beta.1", "1.2.3+build.5", "1.2.3.4" };

        foreach (var input in cases)
        {
            var ok = SemanticVersion.TryParse(input, out var parsed);
            await ok.Should().BeTrue();
            await parsed.Should().NotBeNull();
            await parsed!.ToString().Should().BeEqualTo(input);
        }
    }

    [Test]
    public async Task TryParse_ShouldParseNumericSegments()
    {
        var cases = new (string Input, int Major, int Minor, int Patch)[]
        {
            ("1.2", 1, 2, 0),
            ("1.2.3", 1, 2, 3),
            ("v1.2.3", 1, 2, 3),
            ("v26.3.27", 26, 3, 27),
            ("1.14.3", 1, 14, 3),
            // 第 4 段被忽略，与构造函数既有行为一致
            ("1.2.3.4", 1, 2, 3),
        };

        foreach (var (input, major, minor, patch) in cases)
        {
            var ok = SemanticVersion.TryParse(input, out var parsed);
            await ok.Should().BeTrue();

            var expected = new SemanticVersion(major, minor, patch);
            await parsed!.CompareTo(expected).Should().BeEqualTo(0);
        }
    }
}
