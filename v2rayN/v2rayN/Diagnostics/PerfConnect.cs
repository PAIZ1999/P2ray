using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Handler.Builder;
using ServiceLib.Manager;

namespace v2rayN.Diagnostics;

/// <summary>
/// 性能诊断用：连上当前节点后**保持运行**，让外部工具（curl）测时延与吞吐。
///
/// 与 --selftest 的区别：selftest 连上→造流量→退出；这个只连上、不退出、不造流量，
/// 避免自检自己的流量干扰测量。
/// </summary>
public static class PerfConnect
{
    public static async Task<bool> ConnectAsync()
    {
        try
        {
            var cfg = AppManager.Instance.Config;
            var models = await AppManager.Instance.ProfileModels(cfg.SubIndexId, "");
            var node = models?.FirstOrDefault();
            if (node is null)
            {
                Logging.SaveLog("perf-connect: 没有可用节点");
                return false;
            }

            cfg.IndexId = node.IndexId;
            var profile = await ConfigHandler.GetDefaultServer(cfg);
            if (profile is null)
            {
                Logging.SaveLog("perf-connect: GetDefaultServer 返回空");
                return false;
            }

            var all = await CoreConfigContextBuilder.BuildAll(cfg, profile);
            if (!all.Success)
            {
                Logging.SaveLog("perf-connect: 配置构建失败");
                return false;
            }

            await CoreManager.Instance.Init(cfg, (_, _) => Task.CompletedTask);
            await CoreManager.Instance.LoadCore(all.MainResult.Context, all.PreSocksResult?.Context);
            Logging.SaveLog($"perf-connect: 已连接 {node.Remarks} core={AppManager.Instance.RunningCoreType}");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("perf-connect", ex);
            return false;
        }
    }
}
