using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace IndustrialTcpSession.Wpf;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private TcpMonitor? _monitor;
    private MonitorSnapshot? _last;
    private CancellationTokenSource? _stop;
    private Task? _run;
    private bool _stopping, _closing, _allowClose;
    public ObservableCollection<string> History { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Scenario.ItemsSource = Enum.GetValues<DemoScenario>();
        Scenario.SelectedItem = DemoScenario.Normal;
        _refresh.Tick += (_, _) => { if (!_stopping && _monitor is not null) Render(_monitor.Snapshot); };
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_run is not null || _closing || _stopping) return;
        Start.IsEnabled = Scenario.IsEnabled = false;
        Stop.IsEnabled = true;
        _last = null;
        _stop = new CancellationTokenSource();
        _monitor = new TcpMonitor();
        _run = RunDemoAsync((DemoScenario)Scenario.SelectedItem, _monitor, _stop.Token);
        _refresh.Start();
        try { await _run; }
        catch (Exception error)
        {
            // The application boundary observes unexpected setup/cleanup errors too.
            AddHistory($"{error.GetType().Name}: {error.Message}");
            Detail.Text = error.Message;
        }
        finally
        {
            _refresh.Stop();
            Render(_monitor.Snapshot);
            Temperature.Text = "温度：—（监控结束，无有效读数）";
            _run = null;
            _stop.Dispose();
            _stop = null;
            Stop.IsEnabled = false;
            Start.IsEnabled = Scenario.IsEnabled = !_closing && !_stopping;
        }
    }

    private async Task RunDemoAsync(DemoScenario scenario, TcpMonitor monitor, CancellationToken stop)
    {
        await using var device = new LoopbackDevice(scenario);
        AddHistory($"本机教学设备：{device.Endpoint}，场景 {scenario}。");
        await monitor.RunAsync(device.Endpoint, stop);
        // Stops and awaits the in-process server after the monitor fully exits.
    }

    private async void OnStop(object sender, RoutedEventArgs e) => await StopAsync();

    private async Task StopAsync()
    {
        _stopping = true;
        Start.IsEnabled = Stop.IsEnabled = Scenario.IsEnabled = false;
        Temperature.Text = "温度：—（正在停止，读数失效）";
        Task? run = _run;
        _stop?.Cancel();
        try { if (run is not null) await run; }
        catch (Exception) { /* Same task is observed and reported by OnStart. */ }
        finally
        {
            _stopping = false;
            Start.IsEnabled = Scenario.IsEnabled = !_closing && _run is null;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        await StopAsync();
        _refresh.Stop();
        _allowClose = true;
        Close();
    }

    private void Render(MonitorSnapshot snapshot)
    {
        State.Text = $"{snapshot.State} · 连续失败 {snapshot.ConsecutiveFailures}";
        Detail.Text = snapshot.Message;
        Metrics.Text = $"连接尝试 {snapshot.Attempt} · 会话代次 {snapshot.Generation} · 忽略响应 {snapshot.IgnoredReplies}";
        TimeSpan? age = snapshot.SampleAge;
        Temperature.Text = snapshot.State == LinkState.Online && snapshot.Temperature is decimal value
            && age is TimeSpan elapsed && elapsed <= TimeSpan.FromSeconds(3)
            ? $"温度：{value.ToString("0.0", CultureInfo.InvariantCulture)} °C · {elapsed.TotalSeconds:0.0} 秒前"
            : "温度：—（尚无有效读数或数据已过期）";
        if (!ReferenceEquals(snapshot, _last))
        {
            AddHistory($"{snapshot.State} #{snapshot.Generation}: {snapshot.Message}");
            _last = snapshot;
        }
    }

    private void AddHistory(string text)
    {
        History.Add($"{DateTime.Now:HH:mm:ss.fff} {text}");
        while (History.Count > 100) History.RemoveAt(0);
    }
}
