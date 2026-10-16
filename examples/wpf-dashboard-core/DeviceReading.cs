using System.ComponentModel;

namespace WpfDashboardCore;

public sealed class DeviceReading(ReadingSnapshot snapshot, int threshold) : INotifyPropertyChanged
{
    private bool _isAlert = snapshot.Temperature >= threshold;

    public int Id => snapshot.Id;
    public string Device => snapshot.Device;
    public int Temperature => snapshot.Temperature;
    public bool IsAlert => _isAlert;
    public string AlertText => IsAlert ? "报警" : "正常";
    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateThreshold(int threshold)
    {
        bool next = Temperature >= threshold;
        if (_isAlert == next) return;
        _isAlert = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAlert)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlertText)));
    }
}
