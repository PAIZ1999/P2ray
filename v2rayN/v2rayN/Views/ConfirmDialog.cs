using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace v2rayN.Views;

/// <summary>
/// 深色主题的确认框，替代系统 MessageBox。
///
/// WPF 的 MessageBox 由系统绘制，永远是浅色背景 + 系统蓝问号图标，
/// 在深色界面里像贴了一张白纸。这里用 DialogHost 承载一个与卡片同色的面板，
/// 按钮复用 P2ray 的按钮样式，与其余界面保持一致。
/// </summary>
public static class ConfirmDialog
{
    public const string HostIdentifier = "RootDialog";

    /// <summary>
    /// 弹确认框。返回 true 表示用户点了确认。
    /// 没有可用的 DialogHost 时回退到系统 MessageBox，避免"点了没反应"。
    /// </summary>
    public static async Task<bool> ShowAsync(string message, string confirmText = "删除", string cancelText = "取消")
    {
        try
        {
            var result = await DialogHost.Show(BuildContent(message, confirmText, cancelText), HostIdentifier);
            return result is true;
        }
        catch
        {
            return MessageBox.Show(message, ProductInfo.AppName,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
    }

    private static Border BuildContent(string message, string confirmText, string cancelText)
    {
        var text = new TextBlock
        {
            Style = (Style)Application.Current.FindResource("P2rayBody"),
            Text = message,
            TextWrapping = TextWrapping.Wrap,
        };

        var cancel = new Button
        {
            Style = (Style)Application.Current.FindResource("P2raySecondaryButton"),
            Content = cancelText,
            MinWidth = 76,
        };
        cancel.Click += (_, _) => DialogHost.Close(HostIdentifier, false);

        var confirm = new Button
        {
            Style = (Style)Application.Current.FindResource("P2rayPrimaryButton"),
            Content = confirmText,
            MinWidth = 76,
            Margin = new Thickness(8, 0, 0, 0),
        };
        confirm.Click += (_, _) => DialogHost.Close(HostIdentifier, true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);

        var panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(buttons);

        return new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("P2raySurfaceBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22, 20, 22, 18),
            MinWidth = 320,
            MaxWidth = 420,
            Child = panel,
        };
    }
}
