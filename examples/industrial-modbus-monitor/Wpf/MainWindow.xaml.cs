using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using IndustrialModbus;

namespace IndustrialModbusMonitor.Wpf;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private ModbusMonitor? _monitor;
    private ModbusSnapshot? _last;
    private CancellationTokenSource? _stop;
    private Task? _run;
    private bool _stopping, _closing, _allowClose;
    public ObservableCollection<string> History { get; } = new();

    public MainWindow()
    {
        InitializeComponent(); DataContext = this;
        Fault.ItemsSource = new[] { DeviceFault.None, DeviceFault.RejectWrites,
            DeviceFault.ApplyWriteThenDropOnce, DeviceFault.AdjustSetpoint };
        Fault.SelectedItem = DeviceFault.None;
        _refresh.Tick += (_, _) => { if (!_stopping && _monitor is not null) Render(_monitor.Snapshot); };
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_run is not null || _closing || _stopping) return;
        Start.IsEnabled = Fault.IsEnabled = false; Stop.IsEnabled = true;
        _stop = new CancellationTokenSource(); _monitor = new ModbusMonitor(); _last = null;
        _run = RunDemoAsync((DeviceFault)Fault.SelectedItem, _monitor, _stop.Token);
        _refresh.Start();
        try { await _run; }
        catch (Exception error) { AddHistory($"{error.GetType().Name}: {error.Message}"); }
        finally
        {
            _refresh.Stop(); Render(_monitor.Snapshot);
            _run = null; _stop.Dispose(); _stop = null;
            Stop.IsEnabled = Write.IsEnabled = false;
            Values.Text = "无有效采样（轮询结束）";
            Start.IsEnabled = Fault.IsEnabled = !_closing && !_stopping;
        }
    }

    private async Task RunDemoAsync(DeviceFault fault, ModbusMonitor monitor, CancellationToken stop)
    {
        await using var device = new LoopbackModbusDevice(fault);
        AddHistory($"本机设备 {device.Endpoint}，模式 {fault}。" );
        await monitor.RunAsync(device.Endpoint, stop);
    }

    private void OnWrite(object sender, RoutedEventArgs e)
    {
        if (_stopping || _closing || _monitor is null) return;
        if (!_monitor.TryQueueTarget(Target.Text, out string error)) { Receipt.Text = error; return; }
        Render(_monitor.Snapshot);
    }

    private async void OnStop(object sender, RoutedEventArgs e) => await StopAsync();

    private async Task StopAsync()
    {
        _stopping = true; Start.IsEnabled = Stop.IsEnabled = Write.IsEnabled = Fault.IsEnabled = false;
        Values.Text = "正在停止，读数失效；已发送的写入不能靠停止撤回。";
        Task? run = _run; _stop?.Cancel();
        try { if (run is not null) await run; }
        catch (Exception) { /* OnStart reports the same task. */ }
        finally { _stopping = false; Start.IsEnabled = Fault.IsEnabled = !_closing && _run is null; }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; await StopAsync(); _refresh.Stop(); _allowClose = true; Close();
    }

    private void Render(ModbusSnapshot snapshot)
    {
        bool fresh = snapshot.State == MonitorState.Polling && snapshot.Sample is DeviceSample sample
            && sample.Age <= TimeSpan.FromSeconds(3);
        State.Text = $"{snapshot.State} · 连续读取失败 {snapshot.ReadFailures}";
        Detail.Text = snapshot.Message;
        Write.IsEnabled = fresh && !snapshot.WriteBusy && !_stopping && !_closing;
        if (fresh && snapshot.Sample is DeviceSample data)
        {
            Values.Text = $"温度 {data.Temperature:0.0} °C · Float {data.FloatValue:0.0} · 目标 {data.Target:0.0}\n" +
                $"Ready={data.Ready} · Mode={data.Mode} · Count={data.Count} · {data.Age.TotalSeconds:0.0} 秒前";
            Raw.Text = $"IR0～2 原始寄存器：{data.RawHex}；Float32 契约 ABCD";
        }
        else { Values.Text = "无有效采样或数据已过期"; Raw.Text = "IR0～2 原始寄存器：—；Float32 契约 ABCD"; }
        Receipt.Text = snapshot.Write is WriteReceipt receipt
            ? $"{receipt.State} · 期望原始值 {receipt.Expected} · 读回 {receipt.ReadBack?.ToString() ?? "—"}：{receipt.Message}"
            : "尚无写入。";
        if (!ReferenceEquals(snapshot, _last))
        {
            if (snapshot.State != _last?.State) AddHistory($"状态 {snapshot.State}：{snapshot.Message}");
            if (snapshot.Write != _last?.Write && snapshot.Write is WriteReceipt write)
                AddHistory($"写入 {write.Id.ToString("N")[..8]} {write.State}：{write.Message}");
            _last = snapshot;
        }
    }

    private void AddHistory(string message)
    {
        History.Add($"{DateTime.Now:HH:mm:ss.fff} {message}");
        while (History.Count > 100) History.RemoveAt(0);
    }
}
