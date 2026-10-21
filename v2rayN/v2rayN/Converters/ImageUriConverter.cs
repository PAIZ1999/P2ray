using System.Globalization;
using System.Windows.Data;

namespace v2rayN.Converters;

/// <summary>
/// pack URI 字符串转 ImageSource，供 ListBox/ItemsControl 的 ItemTemplate 逐项加载图标。
///
/// 为什么要有它：站点图标的 URI 挂在每个数据项上（NetProbeResult.IconUri），
/// 不是 ViewModel 的单个属性，没法用 OneWayBind 一次性转换，只能在模板里过转换器。
///
/// 加载失败返回 null，让图片位置留空 —— 素材缺失不该让整页崩掉。
/// </summary>
public class ImageUriConverter : IValueConverter
{
    public static readonly ImageUriConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string uri || uri.Length == 0)
        {
            return null;
        }

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            // OnLoad：立刻读进内存并释放文件句柄，否则 GC 之前资源一直被占
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
