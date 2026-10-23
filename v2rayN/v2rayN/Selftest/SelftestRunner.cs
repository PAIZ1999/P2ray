using System.Net;
using System.Net.Http;
using System.Text.Json;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Helper;
using ServiceLib.Manager;
using ServiceLib.ViewModels;

namespace v2rayN.Selftest;

/// <summary>
/// 隐藏的自动化自检入口：<c>P2ray.exe --selftest</c>
///
/// 存在的理由：这个项目里最关键的几条链路（能否连上、连上后流量统计有没有数）
/// 只能靠人在界面上点。而界面自动化在这个环境里不可靠（UIA 点不动、坐标点击
/// 会落到别的控件上），导致"验证"这一步反复卡住。
///
/// 这里用代码走一遍用户的操作：选第一个节点 → 连接 → 等一段时间 → 采样数据库，
/// 把结果写成 JSON 再退出。它不是产品功能，也不影响正常启动。
/// </summary>
public static class SelftestRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var outPath = Path.Combine(Utils.GetConfigPath(""), "selftest.json");
        var result = new Dictionary<string, object?>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var cfg = AppManager.Instance.Config;
            result["startedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // 1) 选一个节点（按配置里已有的顺序取第一个）
            var models = await AppManager.Instance.ProfileModels(cfg.SubIndexId, "");
            var first = models?.FirstOrDefault();
            if (first is null)
            {
                result["error"] = "没有可用节点";
                return await FinishAsync(outPath, result, 2);
            }

            cfg.IndexId = first.IndexId;
            result["node"] = first.Remarks;
            result["indexId"] = first.IndexId;
            result["coreTypeVless"] = cfg.CoreTypeItem?.FirstOrDefault(t => t.ConfigType == EConfigType.VLESS)?.CoreType.ToString();

            // 2) 用和界面「连接」按钮同一条命令启动内核。
            //    必须在 UI 线程执行：命令内部会改 ReactiveUI 属性，进而更新控件；
            //    在后台线程调用会抛"调用线程无法访问此对象"（已实测）。
            //    注意 Dispatcher.InvokeAsync 传 async 委托时返回的是 Task 的 Task，
            //    要 await 两次才能真正等到命令结束，否则会误判为"已发起"。
            var op = System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => StatusBarViewModel.Instance.SystemProxySetCmd.Execute().FirstOrDefaultAsync());
            await await op;
            result["connectIssuedMs"] = sw.ElapsedMilliseconds;

            // 3) 等内核起来（本地端口开始监听）
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            var listening = false;
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(500);
                if (await IsListening(port))
                {
                    listening = true;
                    break;
                }
            }

            result["localPort"] = port;
            result["listening"] = listening;
            result["listeningMs"] = sw.ElapsedMilliseconds;
            result["runningCore"] = AppManager.Instance.RunningCoreType.ToString();

            if (!listening)
            {
                return await FinishAsync(outPath, result, 3);
            }

            // 4) 产生一些流量：走本地代理端口下载，验证统计链路
            var before = await CountTrafficRowsAsync();
            await GenerateTrafficAsync(port);
            await Task.Delay(6000);
            var after = await CountTrafficRowsAsync();

            result["trafficRowsBefore"] = before;
            result["trafficRowsAfter"] = after;
            result["trafficRowsGrew"] = after > before;

            return await FinishAsync(outPath, result, after > before ? 0 : 4);
        }
        catch (Exception ex)
        {
            result["error"] = ex.ToString();
            return await FinishAsync(outPath, result, 1);
        }
        finally
        {
            result["elapsedMs"] = sw.ElapsedMilliseconds;
        }
    }

    private static async Task<int> FinishAsync(string path, Dictionary<string, object?> result, int exitCode)
    {
        try
        {
            result["exitCode"] = exitCode;
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Logging.SaveLog($"selftest -> {path} exit={exitCode}");
        }
        catch
        {
            // 结果写不出来也不影响退出码
        }

        return exitCode;
    }

    private static async Task<bool> IsListening(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await client.ConnectAsync("127.0.0.1", port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>通过本地代理端口拉点数据，触发流量统计。</summary>
    private static async Task GenerateTrafficAsync(int port)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy($"socks5://127.0.0.1:{port}"),
                UseProxy = true,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            await client.GetStringAsync("https://speed.cloudflare.com/__down?bytes=2000000");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("selftest.GenerateTraffic", ex);
        }
    }

    private static async Task<int> CountTrafficRowsAsync()
    {
        try
        {
            var rows = await SQLiteHelper.Instance.TableAsync<ServiceLib.Models.Entities.TrafficStatItem>().ToListAsync();
            return rows.Count;
        }
        catch
        {
            return -1;
        }
    }
}
