using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace v2rayN.Converters;

/// <summary>
/// 把连通性探测的三态结果转成延迟文字的颜色：
/// null 表示还没测（中性灰），true 成功（语义绿），false 失败（警示红）。
///
/// 不用 MultiBinding + DataTrigger 是因为这里只需要一个颜色，
/// 转换器比模板触发器更好读，也避免每个小卡重复一遍触发器。
/// </summary>
public class ProbeStateBrushConverter : IValueConverter
{
    public static readonly ProbeStateBrushConverter Instance = new();

    private static readonly Brush OkBrush = Freeze(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush FailBrush = Freeze(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly Brush IdleBrush = Freeze(Color.FromRgb(0x8E, 0x8E, 0x93));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 用 bool? 区分"还没测"和"测了但失败"：两者都是 Ok == false，
        // 只靠布尔值画出来一模一样，用户会以为整片网络都断了。
        return value switch
        {
            true => OkBrush,
            false => FailBrush,
            _ => IdleBrush,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
