using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace v2rayN.Converters;

/// <summary>
/// 根据路由模式名称选择图标，让分段按钮能一眼区分三种模式。
/// 名称来自上游，形如 "V4-绕过大陆(Whitelist)"。
/// </summary>
public class RoutingKindConverter : IValueConverter
{
    public static readonly RoutingKindConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var raw = value as string ?? string.Empty;
        var s = raw.ToLowerInvariant();

        if (s.Contains("全局") || s.Contains("global"))
        {
            // 全部走代理 = 整个地球
            return PackIconKind.Earth;
        }

        if (s.Contains("黑名单") || s.Contains("blacklist"))
        {
            // 只有列表内的站点走代理 = 清单
            return PackIconKind.FormatListBulleted;
        }

        if (s.Contains("绕过") || s.Contains("whitelist"))
        {
            // 按规则分流 = 分叉箭头
            return PackIconKind.ArrowSplitVertical;
        }

        return PackIconKind.TuneVariant;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
