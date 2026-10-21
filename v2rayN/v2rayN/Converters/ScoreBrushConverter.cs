using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace v2rayN.Converters;

/// <summary>
/// 信任度数值 0-100 转颜色，用于 IP 评分。
///
/// 阈值与 <see cref="ServiceLib.Models.Dto.IpScoreResult.TrustLevel"/> 的分档保持一致，
/// 否则会出现"文字写良好、颜色是红的"这种自相矛盾。
/// </summary>
public class ScoreBrushConverter : IValueConverter
{
    public static readonly ScoreBrushConverter Instance = new();

    private static readonly Brush ExcellentBrush = Freeze(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush GoodBrush = Freeze(Color.FromRgb(0x7B, 0xD9, 0x6B));
    private static readonly Brush FairBrush = Freeze(Color.FromRgb(0xFF, 0x9F, 0x0A));
    private static readonly Brush PoorBrush = Freeze(Color.FromRgb(0xFF, 0x6B, 0x2E));
    private static readonly Brush BadBrush = Freeze(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly Brush UnknownBrush = Freeze(Color.FromRgb(0x8E, 0x8E, 0x93));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int score)
        {
            return UnknownBrush;
        }

        return score switch
        {
            >= 80 => ExcellentBrush,
            >= 60 => GoodBrush,
            >= 40 => FairBrush,
            >= 20 => PoorBrush,
            > 0 => BadBrush,
            // 0 分一律当"没查到"，不显示成极差 —— 接口失败和真实 0 分无法区分
            _ => UnknownBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
