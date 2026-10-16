using System.Collections.Specialized;
using WpfBindingNotifications;

var model = new ReadingsViewModel();
var changedProperties = new List<string?>();
model.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);
model.PanelTitle = "实时读数";
model.PanelTitle = "实时读数";
Check(changedProperties.SequenceEqual([nameof(ReadingsViewModel.PanelTitle)]),
    "view-model property changes once for a new value");

var collectionChanges = new List<NotifyCollectionChangedAction>();
model.Readings.CollectionChanged += (_, e) => collectionChanges.Add(e.Action);
model.AddReading();
model.RemoveLastReading();
Check(collectionChanges.SequenceEqual([
    NotifyCollectionChangedAction.Add,
    NotifyCollectionChangedAction.Remove
]), "collection add/remove have their own notifications");

var first = model.Readings[0];
var itemChanges = new List<string?>();
first.PropertyChanged += (_, e) => itemChanges.Add(e.PropertyName);
model.IncreaseFirstTemperature();
Check(first.Temperature == 73 &&
      itemChanges.SequenceEqual([nameof(ReadingItem.Temperature)]),
    "item property change is notified by the item");
Check(collectionChanges.Count == 2,
    "changing an item property is not a collection add/remove event");
Console.WriteLine("属性、集合与集合项通知验证通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
