using System.Diagnostics;
using System.Text;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Helper;
using ServiceLib.Models.Entities;

namespace v2rayN.Diagnostics;

/// <summary>
/// 诊断探针：在 **UI 线程** 与 **线程池** 上各跑一次同样的数据库查询，比较耗时。
///
/// 为什么需要它：本项目多次出现"文件写完了、数据库那步不回来、日志里也没异常"的现象
/// （订阅更新、内核版本读取、自检连接、设置页初始化）。此前每次都换条路绕过去，
/// 没有定性。这个探针只写日志、不改行为，用来回答一个问题：
///   **是不是 UI 线程上的异步数据库调用会挂起？**
///
/// 判据（写在 docs/arch/next.md）：同一台机器连续两次启动都超时 ⇒ 产品缺陷；
/// 只有一次 ⇒ 偶发，加超时保护即可。
/// </summary>
public static class UiThreadDbProbe
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(5);

    public static async Task RunAsync(System.Windows.Threading.Dispatcher dispatcher)
    {
        var log = new StringBuilder();
        log.AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId} =====");

        // 1) UI 线程，第一次
        await ProbeAsync(dispatcher, "ui-1", log);
        // 2) UI 线程，第二次（排除首次 JIT / 连接建立的影响）
        await ProbeAsync(dispatcher, "ui-2", log);
        // 3) 线程池对照组：同样的查询，只是不在 UI 线程
        await ProbeAsync(null, "pool", log);

        try
        {
            File.AppendAllText(ProbePath(), log.ToString(), Encoding.UTF8);
        }
        catch
        {
            // 探针不能影响启动
        }
    }

    private static async Task ProbeAsync(System.Windows.Threading.Dispatcher? dispatcher, string label, StringBuilder log)
    {
        var sw = Stopwatch.StartNew();
        string outcome;
        int rows = -1;

        try
        {
            Func<Task<List<ProfileItem>>> work = () => SQLiteHelper.Instance.TableAsync<ProfileItem>().ToListAsync();

            Task<List<ProfileItem>> task;
            if (dispatcher is null)
            {
                task = Task.Run(work);
            }
            else
            {
                // 在 UI 线程上发起，await 之后也会回到 UI 线程 —— 这正是要观察的路径
                task = await dispatcher.InvokeAsync(work);
            }

            var finished = await Task.WhenAny(task, Task.Delay(Watchdog));
            if (finished == task)
            {
                rows = (await task).Count;
                outcome = "ok";
            }
            else
            {
                outcome = "timeout";
            }
        }
        catch (Exception ex)
        {
            outcome = $"exception: {ex.GetType().Name} {ex.Message}";
        }

        sw.Stop();
        log.AppendLine($"{label,-5} outcome={outcome,-12} elapsed={sw.ElapsedMilliseconds,5}ms rows={rows}");
        Logging.SaveLog($"[db-probe] {label} {outcome} {sw.ElapsedMilliseconds}ms rows={rows}");
    }

    private static string ProbePath()
        => Path.Combine(Utils.GetConfigPath(""), "uithread-db-probe.log");
}
