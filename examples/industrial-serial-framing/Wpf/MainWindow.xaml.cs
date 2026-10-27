using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace SerialFraming.Wpf;

public partial class MainWindow : Window
{
    private SerialSession? _session;
    private CancellationTokenSource? _stop;
    private Task? _run;
    private bool _stopping;
    private bool _closing;
    private bool _allowClose;
    public ObservableCollection<string> History { get; } = new();

    public MainWindow() { InitializeComponent(); DataContext = this; }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        if (_run is not null || _closing) return;
        if (!int.TryParse(BaudRate.Text, out int baud) || baud <= 0 || string.IsNullOrWhiteSpace(PortName.Text))
        {
            Status.Text = "请输入端口名和正整数波特率。";
            return;
        }

        bool useReal = RealPort.IsChecked == true;
        string name = PortName.Text.Trim(); // Capture UI values before starting the worker.
        SetInputs(false);
        Disconnect.IsEnabled = true;
        Status.Text = useReal ? $"正在打开 {name}…" : "正在连接内存教学设备（不打开 COM）…";
        Temperature.Text = "温度：—（等待本次会话读数）";
        _stop = new CancellationTokenSource();
        _session = new SerialSession();
        _run = _session.RunAsync(
            () => useReal ? new SerialBytePort(name, baud) : new DemoBytePort(),
            update => Dispatcher.InvokeAsync(() => Apply(update)).Task, _stop.Token);
        try { await _run; }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            // Last UI boundary: record failures, do not silently reconnect or resend.
            AddHistory($"{error.GetType().Name}: {error.Message}");
        }
        finally
        {
            _session = null;
            _run = null;
            _stop.Dispose();
            _stop = null;
            ReadOnce.IsEnabled = Disconnect.IsEnabled = false;
            Temperature.Text = "温度：—（会话已结束，原读数失效）";
            Status.Text = "已断开/会话结束，详细原因见记录。";
            SetInputs(!_closing && !_stopping);
        }
    }

    private void OnRead(object sender, RoutedEventArgs e)
    {
        if (_stopping || _closing || _session?.TryRequest() != true) return;
        ReadOnce.IsEnabled = false;
        Status.Text = "请求已排队；写入成功不等于已收到响应。";
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e) => await StopAsync();

    private async Task StopAsync()
    {
        _stopping = true;
        SetInputs(false);
        ReadOnce.IsEnabled = Disconnect.IsEnabled = false;
        Task? running = _run;
        _stop?.Cancel();
        try
        {
            // Keep pumping Dispatcher: the worker may be awaiting a UI update.
            if (running is not null) await running;
        }
        catch (Exception) { /* OnConnect observes and reports this same task. */ }
        finally { _stopping = false; SetInputs(!_closing && _run is null); }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        await StopAsync();
        _allowClose = true;
        Close();
    }

    private void Apply(SessionUpdate update)
    {
        AddHistory(update.Message);
        if (_stopping || _closing) return;
        Status.Text = update.Message;
        ReadOnce.IsEnabled = update.Kind is UpdateKind.Connected or UpdateKind.Reading;
        if (update.Temperature is decimal value)
            Temperature.Text = $"温度：{value.ToString("0.0", CultureInfo.InvariantCulture)} °C（本次响应）";
    }

    private void AddHistory(string message)
    {
        History.Add($"{DateTime.Now:HH:mm:ss.fff} {message}");
        while (History.Count > 100) History.RemoveAt(0);
    }

    private void SetInputs(bool enabled)
    {
        Connect.IsEnabled = RealPort.IsEnabled = PortName.IsEnabled = BaudRate.IsEnabled = enabled;
    }
}
