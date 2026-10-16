using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using WpfDashboardCore;

namespace WpfReadingDashboard;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel _model = new(new SimulatedReadingFeed());
    private readonly ICollectionView _view;
    private bool _closing;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _model;
        _view = CollectionViewSource.GetDefaultView(_model.Readings);
        _view.Filter = item => item is DeviceReading reading && _model.Matches(reading);
        ReadingsList.ItemsSource = _view;
        _model.PropertyChanged += Model_PropertyChanged;
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.DeviceFilter)
            or nameof(DashboardViewModel.AlertsOnly)
            or nameof(DashboardViewModel.Threshold))
            _view.Refresh();
    }

    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadAsync(false);
    private async void Fail_Click(object sender, RoutedEventArgs e) => await LoadAsync(true);
    private void Cancel_Click(object sender, RoutedEventArgs e) => _model.Cancel();

    private async Task LoadAsync(bool simulateFailure)
    {
        try
        {
            await _model.LoadAsync(simulateFailure);
            Debug.Assert(Dispatcher.CheckAccess());
        }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            MessageBox.Show(this, "发生未预期的界面错误。", "模拟读取");
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_closing) { e.Cancel = true; return; }
        if (!_model.IsBusy) { await _model.CloseAsync(); return; }

        e.Cancel = true;
        _closing = true;
        try { await _model.CloseAsync(); }
        catch (Exception error) { Debug.WriteLine(error); }
        finally { _allowClose = true; Close(); }
    }
}
