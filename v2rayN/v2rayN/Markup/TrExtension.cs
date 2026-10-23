using System.Windows.Markup;

namespace v2rayN.Markup;

/// <summary>
/// XAML 里取本地化文案：<c>Text="{tr:Tr P2NavHome}"</c>。
///
/// 比给每个文案在 ResUI.Designer.cs 里加一个属性更好维护：
/// 新文案只要在 resx 里加一条即可，缺键时回退成键名而不是编译失败。
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => ServiceLib.Resx.ResUIHelper.Get(Key);
}
