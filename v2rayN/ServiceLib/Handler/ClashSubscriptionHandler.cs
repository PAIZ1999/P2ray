namespace ServiceLib.Handler;

/// <summary>
/// 把 Clash 订阅（YAML）导入成一个 mihomo 自定义配置节点。
///
/// 背景：mihomo 只吃 Clash 的 YAML，而程序里没有 Clash 格式的生成器，
/// 所以普通节点走不了 mihomo。原来用户要手动「把订阅存成文件 → 改端口 →
/// 在自定义配置里选 mihomo」，四步操作容易劝退。这里把它压成一步：
/// 订阅更新时识别出 Clash 格式，落盘成 YAML，并维护一个指向它的自定义节点。
/// </summary>
public static class ClashSubscriptionHandler
{
    private static readonly string _tag = "ClashSubscriptionHandler";

    /// <summary>
    /// 判断订阅内容是不是 Clash 配置。
    ///
    /// 只看三个特征，不做完整 YAML 解析：解析失败时抛异常会把整次订阅更新搞挂，
    /// 而这里只需要一个"够可靠"的判断。真正的校验交给 mihomo 启动时。
    /// </summary>
    public static bool LooksLikeClashYaml(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return false;
        }

        var text = content!.TrimStart();

        // JSON 不是 Clash 配置；base64 节点列表也不会有这些键
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            return false;
        }

        var hasProxies = text.Contains("\nproxies:") || text.StartsWith("proxies:");
        var hasGroups = text.Contains("\nproxy-groups:") || text.Contains("\nproxy-providers:");
        var hasRules = text.Contains("\nrules:");

        return hasProxies && (hasGroups || hasRules);
    }

    /// <summary>
    /// 落盘并维护对应的自定义配置节点。返回节点 IndexId；失败返回 null。
    /// </summary>
    public static async Task<string?> ImportAsync(Config config, string subId, string remarks, string content,
        Func<bool, string, Task>? updateFunc)
    {
        try
        {
            var dir = Path.Combine(Utils.GetConfigPath(""), "clash");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var fileName = $"{subId.TrimEx()}.yaml";
            var filePath = Path.Combine(dir, fileName);
            await File.WriteAllTextAsync(filePath, content);

            // IndexId 固定：同一订阅重复更新只覆盖文件，不会越加越多
            var indexId = $"clash-{subId.TrimEx()}";
            var existing = await AppManager.Instance.GetProfileItem(indexId);

            var profile = existing ?? new ProfileItem
            {
                IndexId = indexId,
                ConfigType = EConfigType.Custom,
                CoreType = ECoreType.mihomo,
                Subid = subId,
                IsSub = true,
            };

            profile.Remarks = remarks;
            profile.Address = filePath;

            if (existing is null)
            {
                await ConfigHandler.AddServer(config, profile);
            }
            else
            {
                await SQLiteHelper.Instance.ReplaceAsync(profile);
            }

            await updateFunc?.Invoke(false, $"已识别为 Clash 配置，保存为 mihomo 节点：{remarks}");
            Logging.SaveLog($"{_tag}: 导入 Clash 订阅 {subId} -> {filePath}");
            return indexId;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}.Import", ex);
            await updateFunc?.Invoke(false, $"Clash 配置导入失败：{ex.Message}");
            return null;
        }
    }
}
