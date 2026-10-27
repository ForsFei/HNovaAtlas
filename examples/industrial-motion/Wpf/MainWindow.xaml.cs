using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace IndustrialMotion.Wpf;

public partial class MainWindow : Window
{
    private ManualAxisSession _session = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Button? _heldButton;
    private JogHandle? _handle;
    private double _lastTime, _accumulator, _closeDeadline;
    private bool _closing, _allowClose;
    private string _notice = "依次打开、使能、回零；不会自动执行任何一步。";

    public MainWindow()
    {
        InitializeComponent(); _timer.Tick += OnTick; Render();
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lastTime = _clock.Elapsed.TotalSeconds; UpdateContext(); _timer.Start(); Render();
    }
    private void UpdateContext() => _session.SetContext(!_closing && IsActive, ManualMode.IsChecked == true, Interlocks.IsChecked == true);
    private void OnContextChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded) return;
        UpdateContext();
        if (!_session.AuthorityAvailable) DropCapture("AuthorityLost");
        Render();
    }
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!IsLoaded) return;
        _session.SetContext(false, ManualMode.IsChecked == true, Interlocks.IsChecked == true);
        DropCapture("WindowDeactivated"); Render();
    }
    private void DropCapture(string cause)
    {
        Button? button = _heldButton; _heldButton = null; _handle = null;
        _session.Stop(cause); // Revoke local intent BEFORE releasing capture (which raises more events).
        if (button?.IsMouseCaptured == true) button.ReleaseMouseCapture();
    }
    private void OnJogDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_closing || e.StylusDevice != null || sender is not Button button || _handle != null) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed || !IsActive || !button.CaptureMouse()) return;
        try
        {
            int direction = (string)button.Tag == "-1" ? -1 : 1;
            _handle = _session.Press(direction, .5); _heldButton = button;
            _notice = "只允许当前持有的鼠标点动；没有排队反向或自动续跑。";
        }
        catch (Exception error)
        {
            _notice = error.Message; _handle = null; _heldButton = null;
            if (button.IsMouseCaptured) button.ReleaseMouseCapture();
        }
        Render();
    }
    private void OnJogUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ReferenceEquals(sender, _heldButton)) { DropCapture("MouseReleased"); Render(); }
    }
    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, _heldButton) && ReferenceEquals(e.OriginalSource, _heldButton) && _heldButton?.IsMouseCaptured != true)
        { DropCapture("CaptureLost"); Render(); }
    }
    private void OnAlarmChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _closing) return;
        DropCapture("SimulationAlarmChanged"); _session.InjectInputs(new(Alarm: Alarm.IsChecked == true));
        _notice = "去掉报警勾选只恢复输入；之后仍需显式复位、使能和参考建立。"; Render();
    }
    private void OnAction(object sender, RoutedEventArgs e)
    {
        if (_closing || sender is not Button button) return;
        try
        {
            switch ((string)button.Tag)
            {
                case "Open": _session.Open(); break;
                case "Enable": _session.Enable(); break;
                case "Home": _session.Home(); break;
                case "Stop": DropCapture("OperatorStop"); break;
                case "Reset": _session.ResetFault(); break;
                case "Disable": _session.Disable(); break;
                case "Close": _session.Close(); break;
                case "Rebuild":
                    if (_session.Snapshot.Busy) throw new InvalidOperationException("先观察模拟停止，才能重建内存轴。");
                    DropCapture("RebuildModel"); _session.Dispose(); _session = new();
                    Alarm.IsChecked = false; UpdateContext(); break;
            }
            _notice = "操作已作用于内存模型；状态与结果见下方。";
        }
        catch (Exception error) { _notice = error.Message; }
        Render();
    }
    private void OnTick(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds, elapsed = now - _lastTime; _lastTime = now;
        try
        {
            UpdateContext();
            if (elapsed > .1) { DropCapture("UiObservationGap"); _accumulator = 0; }
            if (_handle is JogHandle handle)
            {
                if (!IsActive || Mouse.LeftButton != MouseButtonState.Pressed || _heldButton?.IsMouseCaptured != true)
                    DropCapture("InputNoLongerHeld");
                else if (!_session.Renew(handle)) DropCapture("HoldExpired");
            }
            else if (Mouse.LeftButton == MouseButtonState.Released) _session.ObserveAllReleased();
            _accumulator += Math.Clamp(elapsed, 0, .05); // Bounded simulator catch-up, never an EtherCAT clock.
            for (int i = 0; i < 5 && _accumulator >= .01; i++)
            {
                if (_session.Snapshot.State != AxisState.Disconnected) _session.Tick(.01);
                _accumulator -= .01;
            }
            if (_handle.HasValue && _session.Held != _handle) DropCapture("BackendHoldLost");
            if (_closing)
            {
                if (!_session.Snapshot.Busy) { _allowClose = true; Close(); return; }
                if (now >= _closeDeadline)
                {
                    _closing = false;
                    _notice = "未在关闭预算内观察到模拟停止；本次关窗已阻止，请核对状态。";
                }
            }
        }
        catch (Exception error)
        {
            DropCapture("UiLoopError"); _notice = error.Message;
        }
        Render();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_closing) { e.Cancel = true; return; }
        DropCapture("WindowClosing"); _session.SetContext(false, false, false);
        if (!_session.Snapshot.Busy) { _allowClose = true; return; }
        e.Cancel = true; _closing = true; _closeDeadline = _clock.Elapsed.TotalSeconds + 3;
        _notice = "已撤销点动，等待模拟停止后关闭；没有对真实设备作出停止承诺。"; Render();
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop(); _timer.Tick -= OnTick; _session.Dispose();
    }
    private void Render()
    {
        AxisSnapshot state = _session.Snapshot; bool connected = state.State != AxisState.Disconnected;
        Operations.IsEnabled = !_closing;
        OpenButton.IsEnabled = !connected;
        EnableButton.IsEnabled = _session.AuthorityAvailable && state.State == AxisState.Disabled;
        HomeButton.IsEnabled = _session.AuthorityAvailable && state.State == AxisState.Ready && state.CommandPosition > 0;
        PositiveJog.IsEnabled = NegativeJog.IsEnabled = _session.Armed || _session.Held != null;
        StopButton.IsEnabled = connected;
        ResetButton.IsEnabled = _session.AuthorityAvailable && state.State == AxisState.Faulted;
        DisableButton.IsEnabled = state.State == AxisState.Ready;
        CloseButton.IsEnabled = connected && !state.Busy;
        RebuildButton.IsEnabled = !state.Busy;
        StatusText.Text = $"状态 {state.State}   结果 {state.Result}\n" +
            $"数据有效 {state.DataValid}   模拟使能 {state.Enabled}   参考有效 {state.Homed}\n" +
            $"指令 {state.CommandPosition:F3} mm   模拟反馈 {state.FeedbackPosition:F3} mm   速度 {state.Velocity:F3} mm/s\n" +
            $"本地持有 {_session.Held.HasValue}   可接受新按下 {_session.Armed}\n" +
            $"故障 {state.Fault ?? "无"}   最近撤销/停止原因 {_session.LastStopCause}";
        NoticeText.Text = _notice;
    }
}
