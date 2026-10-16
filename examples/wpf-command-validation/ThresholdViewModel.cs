using System.ComponentModel;
using System.Globalization;

namespace WpfCommandValidation;

public sealed class ThresholdViewModel : INotifyPropertyChanged, IDataErrorInfo
{
    private string _thresholdText = "50";
    private int _currentThreshold = 50;
    private string _statusText = "尚未修改设备阈值。";

    public ThresholdViewModel()
    {
        ApplyCommand = new RelayCommand(Apply, CanApply);
        CancelCommand = new RelayCommand(Cancel, CanCancel);
    }

    public string ThresholdText
    {
        get => _thresholdText;
        set
        {
            if (_thresholdText == value) return;
            _thresholdText = value;
            OnPropertyChanged(nameof(ThresholdText));
            OnPropertyChanged(nameof(ErrorText));
            ApplyCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public int CurrentThreshold
    {
        get => _currentThreshold;
        private set
        {
            if (_currentThreshold == value) return;
            _currentThreshold = value;
            OnPropertyChanged(nameof(CurrentThreshold));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string ErrorText => GetValidationError();
    public string Error => ErrorText;
    public string this[string columnName] =>
        columnName == nameof(ThresholdText) ? GetValidationError() : string.Empty;

    public RelayCommand ApplyCommand { get; }
    public RelayCommand CancelCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool CanApply() => TryGetThreshold(out int value) && value != CurrentThreshold;
    private bool CanCancel() => ThresholdText != CurrentThreshold.ToString(CultureInfo.InvariantCulture);

    private void Apply()
    {
        if (!TryGetThreshold(out int value) || value == CurrentThreshold) return;
        CurrentThreshold = value;
        StatusText = $"已应用阈值 {value} °C。";
        ThresholdText = value.ToString(CultureInfo.InvariantCulture);
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void Cancel()
    {
        ThresholdText = CurrentThreshold.ToString(CultureInfo.InvariantCulture);
        StatusText = "已取消未提交的编辑；设备阈值没有变化。";
    }

    private bool TryGetThreshold(out int value) =>
        int.TryParse(ThresholdText, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value is >= 0 and <= 100;

    private string GetValidationError()
    {
        if (string.IsNullOrEmpty(ThresholdText)) return "请输入 0–100 的整数。";
        if (!int.TryParse(ThresholdText, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            return "请输入整数，不要输入小数、空格或其他字符。";
        return value is < 0 or > 100 ? "阈值必须在 0–100 之间。" : string.Empty;
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
