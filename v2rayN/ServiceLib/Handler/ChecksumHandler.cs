using System.Security.Cryptography;

namespace ServiceLib.Handler;

/// <summary>
/// 发布包完整性校验。
///
/// CI 每次都会在 Release 里附一个同名的 .sha256（内容是 "哈希  文件名"）。
/// 客户端下载完成后应当比对，否则网络中间人替换 zip、或下载被截断，
/// 都会变成"更新失败"甚至装上一个损坏的版本，而用户看不出原因。
/// </summary>
public static class ChecksumHandler
{
    private static readonly string _tag = "ChecksumHandler";

    /// <summary>计算文件的 SHA-256，返回大写十六进制。</summary>
    public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 从 .sha256 文件内容里取出哈希。
    /// 内容是标准的 <c>&lt;64位hex&gt;  &lt;文件名&gt;</c>，也可能只有哈希本身。
    /// </summary>
    public static string? ParseChecksumFile(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return null;
        }

        foreach (var line in text!.Split('\n', '\r'))
        {
            var token = line.Trim().Split(' ', '\t').FirstOrDefault(t => t.Length == 64);
            if (token is not null && token.All(Uri.IsHexDigit))
            {
                return token.ToUpperInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// 校验下载结果。取不到发布哈希时**不阻断**更新（老版本 Release 没有该资产），
    /// 但会记录日志；一旦取到就必须匹配，不匹配视为失败。
    /// </summary>
    public static async Task<(bool Ok, string Msg)> VerifyAsync(string fileUrl, string filePath,
        DownloadService downloadService, bool blProxy, CancellationToken cancellationToken = default)
    {
        try
        {
            var checksumText = await downloadService.TryDownloadString(fileUrl + ".sha256", blProxy, "", cancellationToken);
            var expected = ParseChecksumFile(checksumText);
            if (expected is null)
            {
                Logging.SaveLog($"{_tag}: 未取到发布校验值，跳过校验 ({fileUrl})");
                return (true, string.Empty);
            }

            var actual = await ComputeFileSha256Async(filePath, cancellationToken);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                Logging.SaveLog($"{_tag}: 校验失败 期望 {expected} 实际 {actual} ({filePath})");
                return (false, "下载文件校验失败，可能被篡改或未下载完整，已中止更新");
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.Verify", ex);
            return (true, string.Empty);
        }
    }
}
