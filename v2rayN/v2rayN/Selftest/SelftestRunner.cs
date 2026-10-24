using System.Net;
using System.Net.Http;
using System.Text.Json;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Helper;
using ServiceLib.Handler.Builder;
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
        // 离线模式：不连节点，用一个假的 clash API 验证统计链路本身
        var fakeIdx = Array.IndexOf(args, "--fake-clash");
        if (fakeIdx >= 0 && args.Length > fakeIdx + 1 && int.TryParse(args[fakeIdx + 1], out var fakePort))
        {
            return await RunFakeClashAsync(fakePort);
        }

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

            // 2) 不经过 UI 的 ViewModel，直接走 CoreManager（和 MainWindowViewModel.Reload 同一套）。
            //    之前试过调用 StatusBarViewModel 的命令：后台线程调用会抛跨线程异常，
            //    回到 UI 线程又因数据库异步而挂起 —— 两条路都不通。
            var profile = await ConfigHandler.GetDefaultServer(cfg);
            if (profile is null)
            {
                result["error"] = "GetDefaultServer 返回空";
                return await FinishAsync(outPath, result, 5);
            }

            var all = await CoreConfigContextBuilder.BuildAll(cfg, profile);
            result["contextBuilt"] = all.Success;
            if (!all.Success)
            {
                result["contextErrors"] = string.Join("; ", all.CombinedValidatorResult.Errors.Select(e => e.ToString()));
                return await FinishAsync(outPath, result, 6);
            }

            await CoreManager.Instance.Init(cfg, (_, _) => Task.CompletedTask);
            await CoreManager.Instance.LoadCore(all.MainResult.Context, all.PreSocksResult?.Context);
            result["loadCoreDone"] = true;
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
            // 诊断：确认 clash API 可达，并记录内核自报的累计值
            var statePort = AppManager.Instance.StatePort2;
            result["statePort"] = statePort;
            var clashBefore = await ReadClashTotalsAsync(statePort);
            result["clashDownBefore"] = clashBefore?.Down;
            result["clashUpBefore"] = clashBefore?.Up;

            var appBefore = await SumAppDownAsync();
            result["appDownBefore"] = appBefore;

            var before = await SumTrafficAsync();
            await GenerateTrafficAsync(port);

            // 统计服务每 20 秒才落盘一次，等不到就显式 flush，避免把"还没落盘"误判成"没统计"
            await Task.Delay(25000);
            await ServiceLib.Services.TrafficStatService.Instance.FlushAsync();

            var after = await SumTrafficAsync();
            result["clashDownAfter"] = (await ReadClashTotalsAsync(statePort))?.Down;

            // 诊断：统计服务用的是 HttpClientHelper，确认它能不能拿到同一份数据
            result["trafficDownBefore"] = before.Down;
            result["trafficDownAfter"] = after.Down;
            result["trafficGrew"] = after.Down > before.Down;
            var appAfter = await SumAppDownAsync();
            result["appDownAfter"] = appAfter;
            var appRows = await SQLiteHelper.Instance.TableAsync<ServiceLib.Models.Entities.AppStatItem>().ToListAsync();
            result["appNames"] = string.Join(",", appRows.Select(r => r.Key.Split('|').Last()));
            result["trafficRowsAfter"] = after.Rows;

            return await FinishAsync(outPath, result, after.Down > before.Down ? 0 : 4);
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
            // 用系统自带 curl：HttpClient 的 socks5 代理在部分环境下不生效（实测没有产生流量）
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = $"-s -o NUL --socks5-hostname 127.0.0.1:{port} --limit-rate 250k --max-time 40 https://speed.cloudflare.com/__down?bytes=5000000 https://www.gstatic.com/generate_204",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null)
            {
                await proc.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("selftest.GenerateTraffic", ex);
        }
    }

    /// <summary>直接问 clash API 要累计值，用来区分"内核没流量"和"统计没记录"。</summary>
    private static async Task<(long Up, long Down)?> ReadClashTotalsAsync(int statePort)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await client.GetStringAsync($"http://127.0.0.1:{statePort}/connections");
            return ServiceLib.Services.TrafficTotalsParser.ParseClashTotals(json);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("selftest.ReadClashTotals", ex);
            return null;
        }
    }
    private static async Task<long> SumAppDownAsync()
    {
        try
        {
            var rows = await SQLiteHelper.Instance.TableAsync<ServiceLib.Models.Entities.AppStatItem>().ToListAsync();
            return rows.Sum(r => r.Down);
        }
        catch
        {
            return -1;
        }
    }

    private static async Task<(long Down, int Rows)> SumTrafficAsync()
    {
        try
        {
            var rows = await SQLiteHelper.Instance.TableAsync<ServiceLib.Models.Entities.TrafficStatItem>().ToListAsync();
            return (rows.Sum(r => r.Down), rows.Count);
        }
        catch
        {
            return (-1, -1);
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

    /// <summary>
    /// 离线验证统计链路：把运行内核伪装成 sing-box、把 clash API 端口指向调用方给的假服务，
    /// 然后只看"数据库有没有增长"。不联网、不需要节点，因此可以进 CI。
    /// </summary>
    private static async Task<int> RunFakeClashAsync(int fakePort)
    {
        var outPath = Path.Combine(Utils.GetConfigPath(""), "selftest.json");
        var result = new Dictionary<string, object?>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            result["mode"] = "fake-clash";
            result["fakePort"] = fakePort;

            // 让 TrafficStatService 走 clash 分支（sing-box）并读到假端口
            AppManager.Instance.RunningCoreType = ECoreType.sing_box;
            var field = typeof(AppManager).GetField("_statePort2", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(AppManager.Instance, fakePort);
            result["statePort"] = AppManager.Instance.StatePort2;

            var before = await SumTrafficAsync();
            ServiceLib.Services.TrafficStatService.Instance.StartPolling();

            // 假服务每被请求一次就把总量加一截，等它涨起来
            await Task.Delay(12000);
            await ServiceLib.Services.TrafficStatService.Instance.FlushAsync();
            var after = await SumTrafficAsync();

            result["trafficDownBefore"] = before.Down;
            result["trafficDownAfter"] = after.Down;
            result["trafficGrew"] = after.Down > before.Down;
            result["trafficRowsAfter"] = after.Rows;
            result["elapsedMs"] = sw.ElapsedMilliseconds;

            return await FinishAsync(outPath, result, after.Down > before.Down ? 0 : 4);
        }
        catch (Exception ex)
        {
            result["error"] = ex.ToString();
            return await FinishAsync(outPath, result, 1);
        }
    }
}