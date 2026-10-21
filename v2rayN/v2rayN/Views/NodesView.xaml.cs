using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReactiveUI;
using ReactiveUI.Primitives.Signals;
using ServiceLib.Models.Entities;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>
/// 节点页：自绘列表。负责把选择/双击/右键操作映射到 ProfilesViewModel 的既有命令。
/// </summary>
public partial class NodesView : ReactiveUserControl<DashboardViewModel>
{
    /// <summary>防止 RadioButton 的 Checked 与 ViewModel 赋值互相触发形成回环。</summary>
    private bool _suppressGroupSync;

    public NodesView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Nodes, v => v.lstNodes.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedNode, v => v.lstNodes.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.NodeGroups, v => v.lstGroups.ItemsSource).DisposeWith(disposables);

            // 数量与空状态必须跟 NodeCount 走，不能用 Nodes：
            // Nodes 是上游集合的同一个实例，原地 ReplaceRange 不会改变属性值。
            this.OneWayBind(ViewModel, vm => vm.NodeCount, v => v.txtCount.Text,
                    count => count > 0 ? $"共 {count} 个节点" : string.Empty)
                .DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.NodeCount, v => v.txtEmpty.Visibility,
                    count => count > 0 ? Visibility.Collapsed : Visibility.Visible)
                .DisposeWith(disposables);

            // 分组选中态：ItemsControl 生成的容器是延迟的，
            // 列表换了就必须等布局跑完再同步，否则找不到 RadioButton。
            this.WhenAnyValue(x => x.ViewModel!.SelectedNodeGroup)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(group =>
                {
                    // 延一帧：集合刚换过时容器还没生成
                    Signal.Every(TimeSpan.FromMilliseconds(80))
                        .Take(1)
                        .ObserveOn(RxSchedulers.MainThreadScheduler)
                        .Subscribe(_ => SyncGroupSelection(group))
                        .DisposeWith(disposables);
                })
                .DisposeWith(disposables);

            // 分组列表本身变化时也要重新同步一次选中态
            if (ViewModel is not null)
            {
                ViewModel.WhenAnyValue(x => x.NodeGroups)
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Subscribe(groups =>
                    {
                        if (groups is INotifyCollectionChanged incc)
                        {
                            incc.CollectionChanged += (_, _) => SyncGroupSelection(ViewModel.SelectedNodeGroup);
                        }

                        SyncGroupSelection(ViewModel.SelectedNodeGroup);
                    })
                    .DisposeWith(disposables);
            }
        });

        // 双击 = 设为默认节点（等价于上游"设为默认服务器"）
        lstNodes.MouseDoubleClick += (_, _) => ApplyToDefault();
        lstNodes.KeyDown += OnListKeyDown;
    }

    /// <summary>
    /// 把 ViewModel 的当前分组同步到按钮选中态。
    /// 用 Tag 反查而不依赖索引位置 —— 容器可能延迟生成，顺序不保证。
    /// </summary>
    private void SyncGroupSelection(SubItem? selected)
    {
        _suppressGroupSync = true;
        try
        {
            foreach (var btn in FindVisualChildren<RadioButton>(lstGroups))
            {
                btn.IsChecked = selected is not null
                                && btn.Tag is SubItem item
                                && item.Id == selected.Id;
            }
        }
        finally
        {
            _suppressGroupSync = false;
        }
    }

    private void Group_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressGroupSync || ViewModel is null)
        {
            return;
        }

        if (sender is RadioButton { Tag: SubItem group })
        {
            ViewModel.SelectedNodeGroup = group;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
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
