using System.Windows;
using ReactiveUI;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

/// <summary>流量统计页：按小时柱状图 + 按天列表。</summary>
public partial class TrafficView : ReactiveUserControl<TrafficViewModel>
{
    public TrafficView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Hours, v => v.lstHours.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Days, v => v.lstDays.ItemsSource).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.TodayUpText, v => v.txtTodayUp.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.TodayDownText, v => v.txtTodayDown.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.WeekUpText, v => v.txtWeekUp.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.WeekDownText, v => v.txtWeekDown.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.PeakHourText, v => v.txtPeak.Text).DisposeWith(disposables);
        });
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        // 清空是不可逆的，问一句再动手
        var ret = MessageBox.Show("将删除全部流量历史记录，该操作不可撤销。继续？",
            "清空统计", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ret != MessageBoxResult.OK)
        {
            return;
        }

        ViewModel.ClearCmd.Execute().Subscribe();
    }
}
