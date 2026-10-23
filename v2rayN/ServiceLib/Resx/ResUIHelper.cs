using System.Globalization;
using System.Resources;

namespace ServiceLib.Resx;

/// <summary>
/// 文案查表的运行时入口。
///
/// 新 UI 的文案迁到 ResUI 后，XAML 里用 <c>{tr:Tr KeyName}</c> 取值。
/// 这样做而不是往 Designer 里加属性，是因为属性要一条条维护、
/// 漏一条就编译不过；走 ResourceManager 则缺键时可以回退。
/// </summary>
public static class ResUIHelper
{
    private static readonly ResourceManager _rm = new(
        "ServiceLib.Resx.ResUI", typeof(ResUIHelper).Assembly);

    /// <summary>取本地化文案；缺键时返回键名本身，便于一眼看出漏翻。</summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        try
        {
            return _rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
        catch
        {
            return key;
        }
    }
}
