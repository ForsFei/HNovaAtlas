using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace WpfAsyncReadings;

public partial class MainWindow : Window
{
    private readonly ReadingSession _session = new(new SimulatedReadingSource());
    private bool _isClosing;
    private bool _allowClose;
    private int _pingCount;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _session;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _session.StartAsync(FailureCheck.IsChecked == true);
            Debug.Assert(Dispatcher.CheckAccess());
        }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            MessageBox.Show(this, "发生未预期的界面错误。", "读取失败");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _session.Cancel();

    private void Ping_Click(object sender, RoutedEventArgs e) =>
        PingText.Text = $"响应测试计数：{++_pingCount}";

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_isClosing)
        {
            e.Cancel = true;
            return;
        }

        if (!_session.IsBusy)
        {
            await _session.CloseAsync();
            return;
        }

        e.Cancel = true;
        _isClosing = true;
        try
        {
            await _session.CloseAsync();
        }
        catch (Exception error)
        {
            Debug.WriteLine(error);
        }
        finally
        {
            _allowClose = true;
            Close();
        }
    }
}
