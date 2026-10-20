using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReactiveUI;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>
/// 节点页：自绘列表。负责把选择/双击/右键操作映射到 ProfilesViewModel 的既有命令。
/// </summary>
public partial class NodesView : ReactiveUserControl<DashboardViewModel>
{
    public NodesView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Nodes, v => v.lstNodes.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedNode, v => v.lstNodes.SelectedItem).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.Nodes, v => v.txtCount.Text,
                    nodes => nodes is { Count: > 0 } ? $"共 {nodes.Count} 个节点" : string.Empty)
                .DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.Nodes, v => v.txtEmpty.Visibility,
                    nodes => nodes is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible)
                .DisposeWith(disposables);
        });

        // 双击 = 设为默认节点（等价于上游"设为默认服务器"）
        lstNodes.MouseDoubleClick += (_, _) => ApplyToDefault();
        lstNodes.KeyDown += OnListKeyDown;
    }

    /// <summary>
    /// 把当前选中项同步到 ProfilesViewModel.SelectedProfiles。
    /// RemoveServerAsync / SetDefaultServer 都依赖该集合，而 ListBox 不会自动写入。
    /// </summary>
    private void SyncSelection()
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        var selected = lstNodes.SelectedItems?.Cast<object>()
            .OfType<ServiceLib.Models.Dto.ProfileItemModel>()
            .ToList() ?? [];

        if (selected.Count == 0 && lstNodes.SelectedItem is ServiceLib.Models.Dto.ProfileItemModel single)
        {
            selected.Add(single);
        }

        vm.SelectedProfiles = selected;
    }

    private void ApplyToDefault()
    {
        if (ViewModel is null)
        {
            return;
        }

        SyncSelection();
        if (ViewModel.SelectedProfiles is not { Count: > 0 })
        {
            return;
        }

        ViewModel.SetDefaultNodeCmd.Execute().Subscribe();
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        SyncSelection();
        if (ViewModel.SelectedProfiles is not { Count: > 0 })
        {
            return;
        }

        // Enter = 设为默认；Delete = 删除
        if (e.Key == Key.Enter)
        {
            ViewModel.SetDefaultNodeCmd.Execute().Subscribe();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            ViewModel.RemoveNodeCmd.Execute().Subscribe();
            e.Handled = true;
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RefreshNodesCmd.Execute().Subscribe();
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddFromClipboardCmd.Execute().Subscribe();
    }

    private void MenuSetDefault_Click(object sender, RoutedEventArgs e)
    {
        ApplyToDefault();
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        SyncSelection();
        if (ViewModel.SelectedProfiles is not { Count: > 0 })
        {
            return;
        }

        ViewModel.RemoveNodeCmd.Execute().Subscribe();
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        SyncSelection();
        ViewModel.EditNodeCmd.Execute().Subscribe();
    }

    private void MenuTestDelay_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        SyncSelection();
        ViewModel.TestNodeDelayCmd.Execute().Subscribe();
    }
}
