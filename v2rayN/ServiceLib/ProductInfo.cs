namespace ServiceLib;

/// <summary>
/// 产品身份的唯一来源。
/// 所有品牌相关的常量都集中在这里，换品牌只需修改本文件。
/// </summary>
public static class ProductInfo
{
    #region 品牌

    /// <summary>产品显示名，用于窗口标题、日志、版本信息。</summary>
    public const string AppName = "P2ray";

    /// <summary>程序集名，同时决定可执行文件名与 avares/pack 资源前缀。</summary>
    public const string AssemblyName = "P2ray";

    /// <summary>上游项目名，用于合规声明与致谢，不参与运行时逻辑。</summary>
    public const string UpstreamName = "v2rayN";

    /// <summary>上游项目地址。</summary>
    public const string UpstreamUrl = "https://github.com/2dust/v2rayN";

    #endregion

    #region 更新通道

    /// <summary>
    /// 自有 GitHub 仓库（owner/repo）。
    /// 客户端更新检查与内核下载都以此为准，不指向上游。
    /// </summary>
    public const string GithubRepo = "PAIZ1999/P2ray";

    /// <summary>Release API 地址。</summary>
    public static string ReleaseApiUrl => $"{Global.GithubApiUrl}/{ResolveRepo()}/releases";

    /// <summary>
    /// Windows x64 发布资产名，必须与 CI 打包脚本产出一致。
    /// </summary>
    public const string ReleaseAssetWin64 = "P2ray-windows-64.zip";

    /// <summary>Windows arm64 发布资产名。</summary>
    public const string ReleaseAssetWinArm64 = "P2ray-windows-arm64.zip";

    /// <summary>发布页地址。</summary>
    public static string ReleasePageUrl => $"{Global.GithubUrl}/{ResolveRepo()}/releases";

    /// <summary>
    /// 实际生效的仓库（owner/repo）。
    /// 优先读配置 ConstItem.SelfUpdateRepo，未配置时回退到编译期常量。
    /// 这样更换更新源只需改配置文件，无需重新编译。
    /// </summary>
    public static string ResolveRepo()
    {
        try
        {
            var configured = AppManager.Instance.Config?.ConstItem?.SelfUpdateRepo;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim().Trim('/');
            }
        }
        catch
        {
            // AppManager 尚未初始化（如单元测试场景）时回退到常量
        }

        return GithubRepo;
    }

    #endregion

    #region 本地存储

    /// <summary>
    /// 数据目录名。Program Files 下无写权限时会落到 %LOCALAPPDATA%\{DataDirName}。
    /// 与上游的 v2rayN 区分，避免两个客户端互相覆盖配置。
    /// </summary>
    public const string DataDirName = "P2ray";

    /// <summary>主配置文件名。</summary>
    public const string ConfigFileName = "p2rayConfig.json";

    /// <summary>SQLite 数据库文件名。</summary>
    public const string DatabaseFileName = "p2rayDB.db";

    /// <summary>用于临时目录探测写权限的标记文件名。</summary>
    public const string NoStoreConfigMarkerFileName = "NotStoreConfigHere.txt";

    /// <summary>
    /// 强制使用 %LOCALAPPDATA% 的环境变量。
    /// 同时作为进程级开关被 <see cref="Utils.HasWritePermission"/> 与 StartupPath 读取。
    /// </summary>
    public const string LocalAppDataEnvName = "P2RAY_LOCAL_APPLICATION_DATA";

    #endregion

    #region 系统集成

    /// <summary>单实例互斥量名称。</summary>
    public const string SingleInstanceMutexName = "P2ray";

    /// <summary>开机自启注册表项名称。</summary>
    public const string AutoRunName = "P2rayAutoRun";

    /// <summary>私有 URI scheme，用于站内分享链接。</summary>
    public const string InnerUriScheme = "p2ray";

    /// <summary>私有 URI 前缀。</summary>
    public const string InnerUriProtocol = InnerUriScheme + "://";

    /// <summary>自更新辅助进程名（不含扩展名）。</summary>
    public const string UpgradeToolName = "P2rayUpgrade";

    /// <summary>WPF 资源前缀，需与启动项目程序集名一致。</summary>
    public const string PackAssetsPrefix = "pack://application:,,,/P2ray;component/Resources/";

    #endregion
}
