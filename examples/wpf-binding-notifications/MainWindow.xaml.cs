using System.Windows;

namespace WpfBindingNotifications;

public partial class MainWindow : Window
{
    private readonly ReadingsViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void AddReading_Click(object sender, RoutedEventArgs e) => _viewModel.AddReading();
    private void RemoveLastReading_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveLastReading();
    private void IncreaseFirstTemperature_Click(object sender, RoutedEventArgs e) =>
        _viewModel.IncreaseFirstTemperature();
}
