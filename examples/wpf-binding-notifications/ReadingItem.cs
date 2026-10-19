using System.ComponentModel;

namespace WpfBindingNotifications;

public sealed class ReadingItem(string deviceId, int temperature) : INotifyPropertyChanged
{
    private int _temperature = temperature;

    public string DeviceId { get; } = deviceId;
    public int Temperature
    {
        get => _temperature;
        set
        {
            if (_temperature == value) return;
            _temperature = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Temperature)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
