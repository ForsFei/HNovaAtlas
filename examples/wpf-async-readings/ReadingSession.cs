using System.ComponentModel;

namespace WpfAsyncReadings;

public sealed class ReadingSession(IReadingSource source) : INotifyPropertyChanged
{
    private CancellationTokenSource? _cancellation;
    private Task? _activeTask;
    private bool _isBusy;
    private bool _isClosed;
    private string _statusText = "尚未读取。";
    private string _lastValueText = "最近读数：无";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            Changed(nameof(IsBusy));
            Changed(nameof(CanStart));
            Changed(nameof(CanCancel));
        }
    }

    public bool CanStart => !_isClosed && !IsBusy;
    public bool CanCancel => !_isClosed && IsBusy && _cancellation?.IsCancellationRequested == false;
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            Changed(nameof(StatusText));
        }
    }
    public string LastValueText
    {
        get => _lastValueText;
        private set
        {
            if (_lastValueText == value) return;
            _lastValueText = value;
            Changed(nameof(LastValueText));
        }
    }
    public Exception? LastError { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task StartAsync(bool simulateFailure)
    {
        if (_isClosed || IsBusy) return _activeTask ?? Task.CompletedTask;

        _cancellation = new CancellationTokenSource();
        LastError = null;
        IsBusy = true;
        StatusText = "读取中（模拟等待约 3 秒）…";
        _activeTask = RunCoreAsync(simulateFailure, _cancellation);
        return _activeTask;
    }

    public void Cancel()
    {
        if (!CanCancel) return;
        _cancellation!.Cancel();
        Changed(nameof(CanCancel));
    }

    public async Task CloseAsync()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            _cancellation?.Cancel();
        }
        if (_activeTask is not null) await _activeTask;
    }

    private async Task RunCoreAsync(bool simulateFailure, CancellationTokenSource cancellation)
    {
        try
        {
            int value = await source.ReadAsync(simulateFailure, cancellation.Token);
            if (!_isClosed)
            {
                LastValueText = $"最近读数：{value} °C";
                StatusText = "读取完成。";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_isClosed) StatusText = "已取消；本次没有更新读数。";
        }
        catch (Exception error)
        {
            LastError = error;
            if (!_isClosed) StatusText = "读取失败（模拟故障），请重试。";
        }
        finally
        {
            cancellation.Dispose();
            _cancellation = null;
            if (_isClosed) _isBusy = false;
            else IsBusy = false;
        }
    }

    private void Changed(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
