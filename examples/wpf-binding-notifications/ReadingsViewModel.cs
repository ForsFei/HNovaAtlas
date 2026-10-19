using System.Collections.ObjectModel;
using System.ComponentModel;

namespace WpfBindingNotifications;

public sealed class ReadingsViewModel : INotifyPropertyChanged
{
    private string _panelTitle = "设备读数";

    public string PanelTitle
    {
        get => _panelTitle;
        set
        {
            if (_panelTitle == value) return;
            _panelTitle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PanelTitle)));
        }
    }

    public ObservableCollection<ReadingItem> Readings { get; } =
    [
        new("泵 A", 72),
        new("泵 B", 68)
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddReading() =>
        Readings.Add(new ReadingItem($"设备 {Readings.Count + 1}", 20 + Readings.Count));

    public void RemoveLastReading()
    {
        if (Readings.Count > 0) Readings.RemoveAt(Readings.Count - 1);
    }

    public void IncreaseFirstTemperature()
    {
        if (Readings.Count > 0) Readings[0].Temperature++;
    }
}
