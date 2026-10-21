namespace ServiceLib.Helper;

/// <summary>
/// 把上游路由项的名称 "V4-绕过大陆(Whitelist)" 压成短标签（规则 / 黑名单 / 全局）。
///
/// 放在 ServiceLib 而不是 UI 层，是因为两处都要用：
/// 分段按钮的显示（视图）和模式说明文案（ViewModel）。
/// 之前只存在于 v2rayN.Converters，ViewModel 引用不到，只能各写一份映射，
/// 两边判断标准迟早会不一致 —— 按钮写"规则"、说明却按"绕过大陆"分支匹配。
/// </summary>
public static class RoutingNameHelper
{
    /// <summary>
    /// 短标签映射。判定放在 Contains 上而不是相等，
    /// 因为内置路由名带版本前缀（V4-），用户自建路由也可能是任意名称。
    /// </summary>
    public static string ToShortName(string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return string.Empty;
        }

        var s = remarks.Trim();

        if (s.Contains("绕过") || s.Contains("Whitelist", StringComparison.OrdinalIgnoreCase))
        {
            return "规则";
        }

        if (s.Contains("黑名单") || s.Contains("Blacklist", StringComparison.OrdinalIgnoreCase))
        {
            return "黑名单";
        }

        if (s.Contains("全局") || s.Contains("Global", StringComparison.OrdinalIgnoreCase))
        {
            return "全局";
        }

        // 用户自建路由：去掉 "V<数字>-" 版本前缀后原样显示
        if (s.Length > 3 && s[0] == 'V' && char.IsAsciiDigit(s[1]) && s[2] == '-')
        {
            s = s[3..];
        }

        return s.Trim();
    }
}
