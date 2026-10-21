using System.Globalization;
using System.Windows.Data;
using ServiceLib.Helper;

namespace v2rayN.Converters;

/// <summary>
/// 分段按钮的短标签转换器。
///
/// 映射逻辑在 ServiceLib.Helper.RoutingNameHelper 里 ——
/// ViewModel 的说明文案也要用同一套判定，两边共用一份才不会写岔。
/// </summary>
public class RoutingNameConverter : IValueConverter
{
    public static readonly RoutingNameConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return RoutingNameHelper.ToShortName(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    /// <summary>保留静态转发，避免调用方（DashboardView.xaml.cs）改动。</summary>
    public static string ToShortName(string? remarks) => RoutingNameHelper.ToShortName(remarks);
}
