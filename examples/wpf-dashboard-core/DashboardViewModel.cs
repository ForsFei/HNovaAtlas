using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace WpfDashboardCore;

public sealed class DashboardViewModel : INotifyPropertyChanged, IDataErrorInfo
{
    private readonly IReadingFeed _feed;
    private CancellationTokenSource? _cancellation;
    private Task? _activeTask;
    private bool _closed;
    private bool _isBusy;
    private int _threshold = 50;
    private string _thresholdText = "50";
    private string _deviceFilter = string.Empty;
    private bool _alertsOnly;
    private string _statusText = "尚未读取；全部数据均为本地模拟。";

    public DashboardViewModel(IReadingFeed feed)
    {
        _feed = feed;
        ApplyThresholdCommand = new RelayCommand(ApplyThreshold, CanApplyThreshold);
        CancelEditCommand = new RelayCommand(CancelEdit, () => ThresholdText != Threshold.ToString(CultureInfo.InvariantCulture));
    }

    public ObservableCollection<DeviceReading> Readings { get; } = [];
    public RelayCommand ApplyThresholdCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Exception? LastError { get; private set; }
    public int Threshold => _threshold;
    public int ReadingCount => Readings.Count;
    public int AlertCount => Readings.Count(reading => reading.IsAlert);
    public bool IsBusy => _isBusy;
    public bool CanLoad => !_closed && !IsBusy;
    public bool CanCancel => !_closed && IsBusy && _cancellation?.IsCancellationRequested == false;
    public string StatusText => _statusText;
    public string Error => ValidationError;
    public string this[string columnName] => columnName == nameof(ThresholdText) ? ValidationError : string.Empty;
    public string ValidationError => GetValidationError();

    public string ThresholdText
    {
        get => _thresholdText;
        set
        {
            if (_thresholdText == value) return;
            _thresholdText = value;
            Changed(nameof(ThresholdText));
            Changed(nameof(ValidationError));
            ApplyThresholdCommand.NotifyCanExecuteChanged();
            CancelEditCommand.NotifyCanExecuteChanged();
        }
    }

    public string DeviceFilter
    {
        get => _deviceFilter;
        set
        {
            if (_deviceFilter == value) return;
            _deviceFilter = value;
            Changed(nameof(DeviceFilter));
        }
    }

    public bool AlertsOnly
    {
        get => _alertsOnly;
        set
        {
            if (_alertsOnly == value) return;
            _alertsOnly = value;
            Changed(nameof(AlertsOnly));
        }
    }

    public bool Matches(DeviceReading reading) =>
        reading.Device.Contains(DeviceFilter.Trim(), StringComparison.OrdinalIgnoreCase)
        && (!AlertsOnly || reading.IsAlert);

    public Task LoadAsync(bool simulateFailure = false)
    {
        if (_closed || IsBusy) return _activeTask ?? Task.CompletedTask;

        _cancellation = new CancellationTokenSource();
        LastError = null;
        SetBusy(true);
        SetStatus("读取中（模拟等待约 2 秒）…");
        _activeTask = LoadCoreAsync(simulateFailure, _cancellation);
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
        if (!_closed)
        {
            _closed = true;
            _cancellation?.Cancel();
        }
        if (_activeTask is not null) await _activeTask;
    }

    private async Task LoadCoreAsync(bool simulateFailure, CancellationTokenSource cancellation)
    {
        try
        {
            IReadOnlyList<ReadingSnapshot> batch = await _feed.ReadAsync(simulateFailure, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested) return;

            Readings.Clear();
            foreach (ReadingSnapshot snapshot in batch)
                Readings.Add(new DeviceReading(snapshot, Threshold));
            Changed(nameof(ReadingCount));
            Changed(nameof(AlertCount));
            SetStatus($"读取完成：{ReadingCount} 条，报警 {AlertCount} 条。");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_closed) SetStatus("已取消；保留上一批读数。");
        }
        catch (Exception error)
        {
            LastError = error;
            if (!_closed) SetStatus("读取失败；保留上一批读数，可重试。");
        }
        finally
        {
            cancellation.Dispose();
            _cancellation = null;
            _isBusy = false;
            if (!_closed)
            {
                Changed(nameof(IsBusy));
                Changed(nameof(CanLoad));
                Changed(nameof(CanCancel));
            }
        }
    }

    private bool CanApplyThreshold() =>
        int.TryParse(ThresholdText, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
        && value is >= 0 and <= 100 && value != Threshold;

    private void ApplyThreshold()
    {
        if (!CanApplyThreshold()) return;
        _threshold = int.Parse(ThresholdText, CultureInfo.InvariantCulture);
        foreach (DeviceReading reading in Readings) reading.UpdateThreshold(Threshold);
        Changed(nameof(Threshold));
        Changed(nameof(AlertCount));
        SetStatus($"本地报警阈值改为 {Threshold} °C；未写入任何设备。");
        ApplyThresholdCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
    }

    private void CancelEdit()
    {
        ThresholdText = Threshold.ToString(CultureInfo.InvariantCulture);
        SetStatus("已撤销未提交的阈值编辑。");
    }

    private string GetValidationError()
    {
        if (string.IsNullOrEmpty(ThresholdText)) return "请输入 0–100 的整数。";
        if (!int.TryParse(ThresholdText, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            return "只能输入整数，不要输入小数或空格。";
        return value is < 0 or > 100 ? "阈值必须在 0–100 之间。" : string.Empty;
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        Changed(nameof(IsBusy));
        Changed(nameof(CanLoad));
        Changed(nameof(CanCancel));
    }

    private void SetStatus(string value)
    {
        _statusText = value;
        Changed(nameof(StatusText));
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
