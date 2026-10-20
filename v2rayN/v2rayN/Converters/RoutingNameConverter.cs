using System.Globalization;
using System.Windows.Data;

namespace v2rayN.Converters;

/// <summary>
/// 把上游路由项的名称 "V4-绕过大陆(Whitelist)" 压成两个字的分段按钮标签。
///
/// 原名称里有两层噪声：
/// 1. "V4-" 是内置路由的版本标记，只对开发者有意义；
/// 2. "(Whitelist)" 是规则类型，对用户没有信息量。
/// 分段按钮宽度有限，中英混排会让三个按钮长短不齐，所以统一成短标签。
/// </summary>
public class RoutingNameConverter : IValueConverter
{
    public static readonly RoutingNameConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return ToShortName(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 短标签映射。判定放在 Contains 上而不是相等，
    /// 因为内置路由名带版本前缀，用户自建路由也可能是任意名称。
    /// 与 ServiceLib.ViewModels.DashboardViewModel.FriendlyRoutingName 保持一致。
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
