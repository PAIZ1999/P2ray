using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    /// <summary>点行内任何位置都展开该行的下拉框——否则只有点中那个小方块才有反应。</summary>
    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject row)
        {
            return;
        }

        var combo = FindChild<ComboBox>(row);
        if (combo is null)
        {
            return;
        }

        combo.IsDropDownOpen = true;
        combo.Focus();
        e.Handled = true;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel?.SaveCmd.Execute().Subscribe();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "内核设置", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Close();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ResetCmd.Execute().Subscribe();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}
