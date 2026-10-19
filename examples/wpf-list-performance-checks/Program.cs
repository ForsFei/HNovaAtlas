using WpfListPerformance;

var thread = new Thread(() =>
{
    var readings = new ReadingList(140);
    Check(readings.Source.Count == 140 && readings.VisibleCount == 140, "初始数据完整");
    Check(readings.View.Cast<Reading>().First().Temperature == 82, "默认温度降序");
    Check(readings.Source[0].Id == 1, "视图排序不改源集合顺序");

    readings.ApplyFilter("设备-07", alertsOnly: true);
    Check(readings.VisibleCount == 1, "设备与报警条件同时生效");
    Check(readings.View.Cast<Reading>().First().Id == 7, "筛选出的数据项正确");
    Check(readings.Source.Count == 140, "筛选不删除源数据");

    readings.ApplyFilter("", alertsOnly: false);
    readings.SortByTemperature(descending: false);
    Check(readings.VisibleCount == 140, "清空筛选恢复视图");
    Check(readings.View.Cast<Reading>().First().Temperature == 18, "升序排序生效");
    Check(readings.Source[0].Id == 1, "源集合仍保持原顺序");
    Console.WriteLine("集合视图的筛选、排序与源数据独立性断言通过。");
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
