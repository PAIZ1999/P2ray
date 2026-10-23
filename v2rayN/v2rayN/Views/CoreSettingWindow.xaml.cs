using ServiceLib;
using System.Windows;
using ReactiveUI;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>内核设置窗口：协议 → 内核的映射。</summary>
public partial class CoreSettingWindow
{
    public CoreSettingWindow()
    {
        InitializeComponent();

        // 标题栏跟着主题走，否则深色界面顶着一条浅色标题栏
        Common.WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Rows, v => v.lstRows.ItemsSource).DisposeWith(disposables);
        });
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SaveCmd.Execute().Subscribe();
        Close();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ResetCmd.Execute().Subscribe();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}
