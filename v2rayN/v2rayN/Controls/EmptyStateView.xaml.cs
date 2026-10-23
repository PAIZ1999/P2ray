using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace v2rayN.Controls;

/// <summary>
/// 统一的空状态视图：图标 + 主文案 + 一句引导。
///
/// 之前四个页面各写一套（一句话的、带引导行的、什么都没有的），
/// 视觉不一致。这里做成一个控件，用属性配置文案与图标。
/// </summary>
public partial class EmptyStateView : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(PackIconKind), typeof(EmptyStateView),
            new PropertyMetadata(PackIconKind.InboxOutline, OnIconChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata("暂无内容", OnTitleChanged));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty, OnHintChanged));

    public EmptyStateView()
    {
        InitializeComponent();
    }

    public PackIconKind Icon
    {
        get => (PackIconKind)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmptyStateView view && e.NewValue is PackIconKind kind)
        {
            view.icoEmpty.Kind = kind;
        }
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmptyStateView view)
        {
            view.txtTitle.Text = e.NewValue as string ?? string.Empty;
        }
    }

    private static void OnHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmptyStateView view)
        {
            view.txtHint.Text = e.NewValue as string ?? string.Empty;
            // 没有引导文案时不要留一行空白
            view.txtHint.Visibility = string.IsNullOrEmpty(view.txtHint.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
