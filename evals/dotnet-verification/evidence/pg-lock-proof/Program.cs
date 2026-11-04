using Npgsql;
var connection=Environment.GetEnvironmentVariable("PGPROOF_CONNECTION") ?? throw new Exception("PGPROOF_CONNECTION required");
var cs=new NpgsqlConnectionStringBuilder(connection);
if(cs.Host!="127.0.0.1"||cs.Port!=56338)throw new Exception("Only assigned disposable PG endpoint permitted");
await using var ds1=NpgsqlDataSource.Create(connection);
await using var ds2=NpgsqlDataSource.Create(connection);
await using(var c1=await ds1.OpenConnectionAsync())
await using(var c2=await ds2.OpenConnectionAsync())
{
 if(c1.ProcessID==c2.ProcessID)throw new Exception("Expected separate backend connections");
 Console.WriteLine($"Independent backend process IDs: {c1.ProcessID} and {c2.ProcessID}");
}
var one=new DistributedTaskCoordinator(ds1);var two=new DistributedTaskCoordinator(ds2);
var key=987654321L;
void Check(bool condition,string evidence){if(!condition)throw new Exception(evidence);Console.WriteLine("PASS "+evidence);}
var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var first=one.TryExecuteExclusiveAsync(key,async ct=>{entered.SetResult();await release.Task.WaitAsync(ct);},CancellationToken.None);
await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
var secondInvoked=false;
var blocked=await two.TryExecuteExclusiveAsync(key,ct=>{secondInvoked=true;return Task.CompletedTask;},CancellationToken.None);
Check(!blocked&&!secondInvoked,"Same key rejected on independent coordinator while first callback active");
release.SetResult();Check(await first,"First callback commits successfully");
Check(await two.TryExecuteExclusiveAsync(key,ct=>Task.CompletedTask,CancellationToken.None),"Same key acquired after first transaction commits");
var failed=false;
try{await one.TryExecuteExclusiveAsync(key,ct=>Task.FromException(new InvalidOperationException("Expected proof failure")),CancellationToken.None);}catch(InvalidOperationException ex)when(ex.Message=="Expected proof failure"){failed=true;}
Check(failed,"Callback exception propagates to caller");
Check(await two.TryExecuteExclusiveAsync(key,ct=>Task.CompletedTask,CancellationToken.None),"Callback exception rollback/disposal releases lock");
using var cancellation=new CancellationTokenSource();
var cancelEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var neverReleased=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var cancelling=one.TryExecuteExclusiveAsync(key,async ct=>{cancelEntered.SetResult();await neverReleased.Task.WaitAsync(ct);},cancellation.Token);
await cancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(!await two.TryExecuteExclusiveAsync(key,ct=>Task.CompletedTask,CancellationToken.None),"Same key remains excluded while cancellable callback active");
cancellation.Cancel();
var cancelled=false;try{await cancelling;}catch(OperationCanceledException){cancelled=true;}
Check(cancelled,"Callback cancellation propagates to caller");
Check(await two.TryExecuteExclusiveAsync(key,ct=>Task.CompletedTask,CancellationToken.None),"Callback cancellation rollback/disposal releases lock");
Console.WriteLine("POSTGRES LOCK PROOF COMPLETED");
