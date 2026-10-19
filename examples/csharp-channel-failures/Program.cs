using System.Threading.Channels;

var timeout = TimeSpan.FromSeconds(5);
var failed = Channel.CreateBounded<int>(1);
var received = new List<int>();

Task producer = ProduceAsync();
Task consumer = ConsumeAsync();

bool producerFailed = false;
try { await producer.WaitAsync(timeout); }
catch (InvalidOperationException ex) when (ex.Message == "injected failure")
{
    producerFailed = true;
}

bool consumerSawFailure = false;
try { await consumer.WaitAsync(timeout); }
catch (InvalidOperationException ex) when (ex.Message == "injected failure")
{
    consumerSawFailure = true;
}

Require(producerFailed && consumerSawFailure, "两端都观察到生产者故障");
Require(received.SequenceEqual([7]), "故障前写入的数据已被读取");
Require(failed.Reader.Completion.IsFaulted, "读端完成状态保留故障");

var cancelled = Channel.CreateBounded<int>(1);
using var source = new CancellationTokenSource();
Task<int> pendingRead = cancelled.Reader.ReadAsync(source.Token).AsTask();
Require(!pendingRead.IsCompleted, "读取正在等待数据");
source.Cancel();

bool readCancelled = false;
try { await pendingRead.WaitAsync(timeout); }
catch (OperationCanceledException) when (source.IsCancellationRequested)
{
    readCancelled = true;
}

Require(readCancelled, "令牌取消了等待中的读取");
Require(!cancelled.Reader.Completion.IsCompleted, "取消读取不等于完成通道");
cancelled.Writer.TryComplete();
await cancelled.Reader.Completion.WaitAsync(timeout);

Console.WriteLine("Channel 故障传播与取消边界断言通过。");

async Task ProduceAsync()
{
    Exception? failure = null;
    try
    {
        await failed.Writer.WriteAsync(7);
        throw new InvalidOperationException("injected failure");
    }
    catch (Exception ex)
    {
        failure = ex;
        throw;
    }
    finally
    {
        failed.Writer.TryComplete(failure);
    }
}

async Task ConsumeAsync()
{
    await foreach (int item in failed.Reader.ReadAllAsync())
        received.Add(item);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
