using System.Globalization;
using System.Windows.Data;

namespace v2rayN.Converters;

/// <summary>
/// 把 Unix 秒级时间戳转成可读的相对时间。
/// 0 或非法值显示"从未更新"。
/// </summary>
public class UnixTimeConverter : IValueConverter
{
    public static readonly UnixTimeConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        long seconds;
        try
        {
            seconds = value switch
            {
                long l => l,
                int i => i,
                _ => 0
            };
        }
        catch
        {
            return "从未更新";
        }

        if (seconds <= 0)
        {
            return "从未更新";
        }

        try
        {
            var time = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
            var span = DateTimeOffset.Now - time;

            if (span.TotalMinutes < 1)
            {
                return "刚刚";
            }

            if (span.TotalHours < 1)
            {
                return $"{(int)span.TotalMinutes} 分钟前";
            }

            if (span.TotalDays < 1)
            {
                return $"{(int)span.TotalHours} 小时前";
            }

            if (span.TotalDays < 30)
            {
                return $"{(int)span.TotalDays} 天前";
            }

            return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "从未更新";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
