var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var second = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
Task<int[]> all = Task.WhenAll(first.Task, second.Task);
var firstError = new InvalidOperationException("source A failed");
var secondError = new ApplicationException("source B failed");
first.SetException(firstError);
Check(!all.IsCompleted, "WhenAll waits for every task, even after a fault");
second.SetException(secondError);
try { await all; throw new Exception("expected a fault"); }
catch (Exception error) when (ReferenceEquals(error, firstError) || ReferenceEquals(error, secondError)) { }
if (!all.IsFaulted || all.Exception is not { InnerExceptions.Count: 2 } aggregate)
    throw new InvalidOperationException("both faults are retained");
Check(aggregate.InnerExceptions.Contains(firstError) && aggregate.InnerExceptions.Contains(secondError),
    "the combined task exposes both source errors");

using var canceledSource = new CancellationTokenSource();
canceledSource.Cancel();
Task<int> canceled = Task.FromCanceled<int>(canceledSource.Token);
Task<int[]> canceledAll = Task.WhenAll(Task.FromResult(1), canceled);
try { await canceledAll; throw new Exception("expected cancellation"); }
catch (OperationCanceledException) { }
Check(canceledAll.IsCanceled, "cancellation wins when there are no faults");
Task<int[]> faultWins = Task.WhenAll(canceled, Task.FromException<int>(firstError));
try { await faultWins; throw new Exception("expected a fault"); }
catch (InvalidOperationException error) when (ReferenceEquals(error, firstError)) { }
Check(faultWins.IsFaulted, "a fault takes precedence over cancellation");

var winnerSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var loserSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
Task<Task<int>> race = Task.WhenAny(winnerSource.Task, loserSource.Task);
winnerSource.SetException(secondError);
Task<int> winner = await race;
Check(ReferenceEquals(winner, winnerSource.Task) && !loserSource.Task.IsCompleted,
    "WhenAny returns the completed task and leaves the loser running");
try { await winner; throw new Exception("expected winner fault"); }
catch (ApplicationException error) when (ReferenceEquals(error, secondError)) { }
loserSource.SetResult(8);
Check(await loserSource.Task == 8, "the loser still needs an owner and observation");

var slow = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
try { await slow.Task.WaitAsync(TimeSpan.Zero); throw new Exception("expected timeout"); }
catch (TimeoutException) { }
Check(!slow.Task.IsCompleted, "WaitAsync timeout does not stop the original task");
slow.SetResult(9);
Check(await slow.Task == 9, "the original task can still complete afterward");

Console.WriteLine("WhenAll、WhenAny 与 WaitAsync 的状态断言全部通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
