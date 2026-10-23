using System.Security.Cryptography;
using System.Text;

namespace ServiceLib.Tests.Handler;

/// <summary>
/// 发布包校验。
///
/// 关键点：
///   1. .sha256 内容是 "哈希  文件名"，要能取出哈希；
///   2. 大写/小写都要能比；
///   3. 取不到校验文件时不能阻断更新（老版本 Release 没这个资产）。
/// </summary>
public class ChecksumHandlerTests
{
    private const string Hash = "926BFEA2CE48A247D6A7CE4CF641980E416202D5E40FE4EE5C949089ACE0E197";

    [Test]
    public async Task ParseChecksumFile_StandardFormat_ShouldExtractHash()
    {
        var text = $"{Hash}  P2ray-windows-64.zip\n";
        await ChecksumHandler.ParseChecksumFile(text).Should().BeEqualTo(Hash);
    }

    [Test]
    public async Task ParseChecksumFile_LowercaseAndCrlf_ShouldStillWork()
    {
        var text = $"{Hash.ToLowerInvariant()}  P2ray-windows-64.zip\r\n";
        await ChecksumHandler.ParseChecksumFile(text).Should().BeEqualTo(Hash);
    }

    [Test]
    public async Task ParseChecksumFile_HashOnly_ShouldWork()
    {
        await ChecksumHandler.ParseChecksumFile(Hash).Should().BeEqualTo(Hash);
    }

    [Test]
    public async Task ParseChecksumFile_EmptyOrGarbage_ShouldReturnNull()
    {
        await ChecksumHandler.ParseChecksumFile(null).Should().BeNull();
        await ChecksumHandler.ParseChecksumFile("").Should().BeNull();
        await ChecksumHandler.ParseChecksumFile("404: Not Found").Should().BeNull();
    }

    [Test]
    public async Task ComputeFileSha256_ShouldMatchKnownContent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"p2ray-hash-{Guid.NewGuid():N}.bin");
        await File.WriteAllTextAsync(path, "p2ray");
        try
        {
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("p2ray")));
            var actual = await ChecksumHandler.ComputeFileSha256Async(path);
            await actual.Should().BeEqualTo(expected);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
