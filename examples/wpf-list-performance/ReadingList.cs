using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace WpfListPerformance;

public sealed class ReadingList
{
    public ObservableCollection<Reading> Source { get; }
    public ICollectionView View { get; }
    public int VisibleCount => View.Cast<Reading>().Count();

    public ReadingList(int count)
    {
        Source = new ObservableCollection<Reading>(
            Enumerable.Range(1, count).Select(i => new Reading(
                i, $"设备-{i % 50:D2}", 18 + i % 65, i % 7 == 0)));
        View = CollectionViewSource.GetDefaultView(Source);
        SortByTemperature(descending: true);
    }

    public void ApplyFilter(string? device, bool alertsOnly)
    {
        string query = device?.Trim() ?? string.Empty;
        View.Filter = item => item is Reading reading
            && (!alertsOnly || reading.IsAlert)
            && reading.Device.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public void SortByTemperature(bool descending)
    {
        using (View.DeferRefresh())
        {
            View.SortDescriptions.Clear();
            View.SortDescriptions.Add(new SortDescription(
                nameof(Reading.Temperature),
                descending ? ListSortDirection.Descending : ListSortDirection.Ascending));
            View.SortDescriptions.Add(new SortDescription(nameof(Reading.Id), ListSortDirection.Ascending));
        }
    }
}
