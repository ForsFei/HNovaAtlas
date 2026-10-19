using System.IO;
using WpfAsyncReadings;

var successSource = new ControlledSource();
var success = new ReadingSession(successSource);
Task first = success.StartAsync(false);
Task duplicate = success.StartAsync(false);
Check(success.IsBusy && !success.CanStart && success.CanCancel, "运行期间可取消、不可重复启动");
Check(successSource.Calls == 1, "重复点击不启动第二次读取");
successSource.Complete(72);
await Task.WhenAll(first, duplicate);
Check(!success.IsBusy && success.CanStart && success.LastValueText.Contains("72"), "成功结果和状态");

var cancelledSource = new ControlledSource();
var cancelled = new ReadingSession(cancelledSource);
Task cancelledTask = cancelled.StartAsync(false);
cancelled.Cancel();
await cancelledTask;
Check(cancelled.StatusText.StartsWith("已取消") && cancelled.LastValueText.Contains("无"), "取消不写入读数");
Check(!cancelled.IsBusy && !cancelled.CanCancel, "取消后恢复空闲");

var failureSource = new ControlledSource();
var failure = new ReadingSession(failureSource);
Task failedTask = failure.StartAsync(true);
failureSource.Fail(new IOException("受控失败"));
await failedTask;
Check(failure.LastError is IOException && failure.StatusText.Contains("失败"), "错误由会话捕获并反馈");
Check(failure.CanStart, "错误后可重试");

var closingSource = new ControlledSource();
var closing = new ReadingSession(closingSource);
Task closingTask = closing.StartAsync(false);
int notificationsAfterCloseRequest = 0;
closing.PropertyChanged += (_, _) => notificationsAfterCloseRequest++;
await closing.CloseAsync();
await closingTask;
Check(notificationsAfterCloseRequest == 0, "关闭请求后不再通知窗口");
Check(!closing.CanStart && !closing.IsBusy, "关闭时取消并等任务结束");
await closing.StartAsync(false);
Check(closingSource.Calls == 1, "关闭后不能重新启动");

Console.WriteLine("异步完成、重复启动、取消、失败与关闭路径断言通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed class ControlledSource : IReadingSource
{
    private readonly TaskCompletionSource<int> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls { get; private set; }

    public Task<int> ReadAsync(bool simulateFailure, CancellationToken cancellationToken)
    {
        Calls++;
        return _result.Task.WaitAsync(cancellationToken);
    }

    public void Complete(int value) => _result.SetResult(value);
    public void Fail(Exception error) => _result.SetException(error);
}
