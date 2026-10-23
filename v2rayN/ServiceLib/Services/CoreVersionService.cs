using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ServiceLib.Services;

/// <summary>
/// 读取已安装内核的版本。
///
/// 三个内核的输出格式各不相同，实测样本：
///   xray      "Xray 26.3.27 (Xray, Penetrates Everything.) ..."
///   sing-box  "sing-box version 1.14.3"
///   mihomo    "Mihomo Meta v1.19.33 windows amd64 with go1.26.10 ..."
/// 所以不能按固定位置取，而是找第一个「带点号的版本串」。
/// </summary>
public static partial class CoreVersionService
{
    [GeneratedRegex(@"\d+\.\d+\.\d+(?:[-.\w]*)")]
    private static partial Regex VersionRegex();

    /// <summary>从 <c>-version</c> 的输出里提取版本号；取不到返回 null。</summary>
    public static string? ParseVersion(string? output)
    {
        if (output.IsNullOrEmpty())
        {
            return null;
        }

        // 只看前几行：后面通常是构建信息，容易混进日期之类的数字
        var head = string.Join('\n', output!.Split('\n').Take(3));
        var match = VersionRegex().Match(head);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// 读取已安装内核的版本；没装或读不出来时返回 null（界面显示「未安装」）。
    ///
    /// 这里直接用 Process 而不走 Utils.GetCliWrapOutput：后者在 UI 线程上
    /// 调用会卡住（实测 20 秒都不返回），而这个方法是从设置页构造里发起的。
    /// 另外加了 5 秒超时，避免某个内核卡住把整页拖死。
    /// </summary>
    public static async Task<string?> GetInstalledVersionAsync(ECoreType type, CancellationToken cancellationToken = default)
    {
        try
        {
            var coreInfo = Manager.CoreInfoManager.Instance.GetCoreInfo(type);
            if (coreInfo?.VersionArg.IsNullOrEmpty() != false)
            {
                return null;
            }

            var exe = Manager.CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out _);
            if (exe.IsNullOrEmpty() || !File.Exists(exe))
            {
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = coreInfo.VersionArg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            // 5 秒还没退出的内核视为异常，直接杀掉，不让设置页一直等
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var text = (await stdoutTask).IsNullOrEmpty() ? await stderrTask : await stdoutTask;
            return ParseVersion(text);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("CoreVersionService.GetInstalledVersion", ex);
            return null;
        }
    }
}