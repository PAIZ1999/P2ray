using System.Windows;
using ReactiveUI;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>
/// 首页内嵌的精简日志面板。
/// 复用 MsgViewModel，但不使用上游 MsgView 的密集工具条。
/// 注意：ShowMsgInteraction 只能有一个处理器，同一时刻只应挂载本视图或 MsgView 之一。
/// </summary>
public partial class LogPanelView : ReactiveUserControl<MsgViewModel>
{
    public LogPanelView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel.ShowMsgInteraction.RegisterHandler(interaction =>
            {
                Append(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });
    }

    private void Append(object? msg)
    {
        if (msg is null)
        {
            return;
        }

        if (txtLog.LineCount > (ViewModel?.NumMaxMsg ?? 500))
        {
            txtLog.Clear();
        }

        txtLog.AppendText(msg.ToString());
        if (togAutoScroll.IsChecked ?? true)
        {
            txtLog.ScrollToEnd();
        }
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        WindowsUtils.SetClipboardData(txtLog.Text);
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        txtLog.Clear();
    }
}
