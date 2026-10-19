using System.IO;
using WpfDashboardCore;

var feed = new ControlledFeed();
var model = new DashboardViewModel(feed);
Check(model.CanLoad && !model.CanCancel && model.ReadingCount == 0, "初始状态");

Task first = model.LoadAsync();
Task duplicate = model.LoadAsync();
Check(feed.Calls == 1 && model.IsBusy && !model.CanLoad && model.CanCancel, "运行中禁止重复读取");
feed.Complete(
    new ReadingSnapshot(1, "设备-01", 40),
    new ReadingSnapshot(2, "设备-02", 65),
    new ReadingSnapshot(3, "设备-03", 85));
await Task.WhenAll(first, duplicate);
Check(model.ReadingCount == 3 && model.AlertCount == 2, "批量读取和报警计数");
Check(model.CanLoad && !model.IsBusy && model.StatusText.Contains("读取完成"), "完成后恢复空闲");

model.ThresholdText = "abc";
Check(!model.ApplyThresholdCommand.CanExecute(null) && model.ValidationError.Length > 0, "无效阈值不能提交");
model.ThresholdText = "101";
Check(!model.ApplyThresholdCommand.CanExecute(null), "超范围阈值不能提交");
model.CancelEditCommand.Execute(null);
Check(model.ThresholdText == "50" && model.Threshold == 50, "取消编辑不改已应用值");
model.ThresholdText = "80";
Check(model.ApplyThresholdCommand.CanExecute(null), "有效阈值可提交");
model.ApplyThresholdCommand.Execute(null);
Check(model.Threshold == 80 && model.AlertCount == 1 && model.Readings[1].AlertText == "正常", "阈值更新逐项状态");
Check(!model.ApplyThresholdCommand.CanExecute(null), "相同阈值不能重复提交");

model.DeviceFilter = " 设备-02 ";
Check(model.Matches(model.Readings[1]) && !model.Matches(model.Readings[0]), "设备名称筛选会修剪空格");
model.DeviceFilter = "sensor-a";
Check(model.Matches(new DeviceReading(new ReadingSnapshot(4, "Sensor-A", 40), 80)),
    "拉丁字母设备名称不区分大小写");
model.DeviceFilter = "设备-02";
model.AlertsOnly = true;
Check(!model.Matches(model.Readings[1]), "仅报警过滤");
model.DeviceFilter = "";
Check(model.Matches(model.Readings[2]) && !model.Matches(model.Readings[1]), "仅报警视图条件");

var cancelFeed = new ControlledFeed();
var cancelling = new DashboardViewModel(cancelFeed);
Task cancelledTask = cancelling.LoadAsync();
cancelling.Cancel();
await cancelledTask;
Check(cancelling.ReadingCount == 0 && cancelling.StatusText.Contains("已取消"), "取消不写入新批次");
Check(cancelling.CanLoad && !cancelling.CanCancel, "取消后可重试");

var failureFeed = new ControlledFeed();
var failing = new DashboardViewModel(failureFeed);
Task failedTask = failing.LoadAsync(simulateFailure: true);
failureFeed.Fail(new IOException("受控故障"));
await failedTask;
Check(failing.LastError is IOException && failing.StatusText.Contains("失败"), "故障被观察并反馈");
Check(failing.CanLoad && failing.ReadingCount == 0, "失败后可重试且无伪数据");

var closeFeed = new ControlledFeed();
var closing = new DashboardViewModel(closeFeed);
Task closingTask = closing.LoadAsync();
int notificationsAfterClose = 0;
closing.PropertyChanged += (_, _) => notificationsAfterClose++;
await closing.CloseAsync();
await closingTask;
Check(notificationsAfterClose == 0 && !closing.CanLoad && closing.ReadingCount == 0,
    "关闭取消当前读取，关闭后不通知也不接受新数据");
await closing.LoadAsync();
Check(closeFeed.Calls == 1, "关闭后不能重新读取");

Console.WriteLine("读取、阈值、筛选、重复启动、取消、故障与关闭断言通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed class ControlledFeed : IReadingFeed
{
    private readonly TaskCompletionSource<IReadOnlyList<ReadingSnapshot>> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls { get; private set; }

    public Task<IReadOnlyList<ReadingSnapshot>> ReadAsync(bool simulateFailure, CancellationToken cancellationToken)
    {
        Calls++;
        return _result.Task.WaitAsync(cancellationToken);
    }

    public void Complete(params ReadingSnapshot[] readings) => _result.SetResult(readings);
    public void Fail(Exception error) => _result.SetException(error);
}
