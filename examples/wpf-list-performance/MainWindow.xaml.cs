using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfListPerformance;

public partial class MainWindow : Window
{
    private readonly ReadingList _readings = new(3000);

    public MainWindow()
    {
        InitializeComponent();
        ReadingsList.ItemsSource = _readings.View;
        Loaded += (_, _) => UpdateDiagnostics();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (DeviceQuery is null || AlertsOnly is null) return;
        _readings.ApplyFilter(DeviceQuery.Text, AlertsOnly.IsChecked == true);
        Dispatcher.BeginInvoke(UpdateDiagnostics, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Sort_Changed(object sender, RoutedEventArgs e)
    {
        if (DescendingSort is null) return;
        _readings.SortByTemperature(DescendingSort.IsChecked == true);
        Dispatcher.BeginInvoke(UpdateDiagnostics, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Measure_Click(object sender, RoutedEventArgs e) => UpdateDiagnostics();

    private void UpdateDiagnostics()
    {
        ReadingsList.UpdateLayout();
        int realized = CountContainers(ReadingsList);
        Diagnostics.Text = $"源项 {_readings.Source.Count:N0} / 视图项 {_readings.VisibleCount:N0} / "
            + $"当前可视树 ListBoxItem {realized:N0}。改变开关后点击“刷新计数”；这是容器快照，不是耗时基准。";
    }

    private static int CountContainers(DependencyObject root)
    {
        int count = root is ListBoxItem ? 1 : 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            count += CountContainers(VisualTreeHelper.GetChild(root, i));
        return count;
    }
}
