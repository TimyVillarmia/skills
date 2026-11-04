using FilterSnippet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Reflection;
using System.Collections.Concurrent;
var connection=Environment.GetEnvironmentVariable("DATAPROOF_SQL") ?? throw new Exception("DATAPROOF_SQL required");
if(new SqlConnectionStringBuilder(connection).InitialCatalog!="DataProof")throw new Exception("Wrong database; only DataProof allowed");
var counter=new CommandCounter();
var options=new DbContextOptionsBuilder<ProofDb>().UseSqlServer(connection).AddInterceptors(counter).Options;
var ct=CancellationToken.None;
await using(var db=new ProofDb(options)) {
 await db.Database.EnsureDeletedAsync();
 await db.Database.EnsureCreatedAsync();
}
void Check(bool ok,string evidence) {if(!ok)throw new Exception(evidence);Console.WriteLine("PASS "+evidence);}
// In-process proof: deterministic barrier keeps first task inside exact original gate until released.
using(var gate=new ResourceGate()) {
 var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 var calls=0;
 gate.Work=async token=>{Interlocked.Increment(ref calls);entered.TrySetResult();await release.Task.WaitAsync(token);};
 var first=gate.AccessAsync(ct);await entered.Task;
 var second=gate.AccessAsync(ct);
 Check(calls==1&&!second.IsCompleted,"ResourceGate excludes competing entrant before release");
 release.SetResult();await Task.WhenAll(first,second);
 Check(calls==2,"ResourceGate releases waiting entrant");
}
var keyed=new KeyedLockManager();
var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var unlock=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var k1=keyed.ExecuteAsync("one",async()=>{held.SetResult();await unlock.Task;},ct);await held.Task;
var k2=keyed.ExecuteAsync("one",()=>Task.CompletedTask,ct);
await keyed.ExecuteAsync("two",()=>Task.CompletedTask,ct);
Check(!k2.IsCompleted,"Keyed lock excludes same key while allowing distinct key");unlock.SetResult();await Task.WhenAll(k1,k2);
for(var i=0;i<1000;i++)await keyed.ExecuteAsync(i.ToString(),()=>Task.CompletedTask,ct);
var dict=(ConcurrentDictionary<string,SemaphoreSlim>)typeof(KeyedLockManager).GetField("_locks",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(keyed)!;
Check(dict.Count==1002,"Keyed lock retains all1002 unique keys; dictionary is not bounded");
var state=new AtomicState();await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>{for(var i=0;i<1000;i++)state.RecordItem();})));
Check(state.Processed==8000&&state.TryStart()&&!state.TryStart(),"Interlocked counter totals8000 and start flag succeeds only once");
await ParallelSample.Run(ct);Check(true,"Original Parallel.ForEachAsync sample executes");
var tenant=Guid.NewGuid();var owner=Guid.NewGuid();
await using(var db=new ProofDb(options)) {
 db.Users.AddRange(new User{Id=Guid.NewGuid(),Email="active@sample",IsActive=true},new User{Id=Guid.NewGuid(),Email="inactive@sample",IsActive=false});
 db.Assets.AddRange(new Asset{Id=Guid.NewGuid(),Name="retained",OwnerId=owner},new Asset{Id=Guid.NewGuid(),Name="archived",OwnerId=owner,IsArchived=true});
 for(var i=0;i<3;i++)db.EagerOrders.Add(new EagerOrder{Id=Guid.NewGuid(),Lines=[new(){Id=Guid.NewGuid(),Quantity=1},new(){Id=Guid.NewGuid(),Quantity=2}]});
 var active=new FilteredOrder{Id=Guid.NewGuid(),TenantId=tenant};var deleted=new FilteredOrder{Id=Guid.NewGuid(),TenantId=tenant,IsDeleted=true};
 db.FilteredChildren.AddRange(new FilteredChild{Id=Guid.NewGuid(),Order=active},new FilteredChild{Id=Guid.NewGuid(),Order=deleted});
 await db.SaveChangesAsync();
}
for(var reverse=0;reverse<2;reverse++) {
 var id=Guid.NewGuid();
 await using(var seed=new ProofDb(options)){seed.Orders.Add(new Order{Id=id,TotalAmount=10});await seed.SaveChangesAsync();}
 await using var a=new ProofDb(options);await using var b=new ProofDb(options);
 var aa=await a.Orders.SingleAsync(x=>x.Id==id);var bb=await b.Orders.SingleAsync(x=>x.Id==id);
 Check(aa.Version.Length==8&&aa.Version.SequenceEqual(bb.Version),"Original Timestamp byte[] receives SQL Server8-byte rowversion");
 aa.TotalAmount=20;bb.TotalAmount=30;
 var winner=reverse==0?a:b;var loser=reverse==0?b:a;
 await winner.SaveChangesAsync();
 var caught=false;try{await ConflictSample.Save(loser,ct);}catch(ConcurrencyConflictException){caught=true;}
 Check(caught,"Original conflict handler rejects stale writer, commit order "+reverse);
 await using var fresh=new ProofDb(options);var persisted=await fresh.Orders.SingleAsync(x=>x.Id==id);
 Check(persisted.TotalAmount==(reverse==0?20:30),"Fresh context sees committed winner, commit order "+reverse);
}
// Deleted-row branch of the exact documented conflict handler.
var deletedId=Guid.NewGuid();
await using(var seed=new ProofDb(options)){seed.Orders.Add(new Order{Id=deletedId,TotalAmount=10});await seed.SaveChangesAsync();}
await using(var stale=new ProofDb(options)) {
 var row=await stale.Orders.SingleAsync(x=>x.Id==deletedId);row.TotalAmount=40;
 await using(var deletion=new ProofDb(options)){await deletion.Orders.Where(x=>x.Id==deletedId).ExecuteDeleteAsync();}
 var caught=false;try{await ConflictSample.Save(stale,ct);}catch(EntityDeletedConflictException){caught=true;}
 Check(caught,"Original conflict handler distinguishes deleted record");
}
await using(var db=new ProofDb(options)) {
 var users=await EfSamples.ReadUsers(db,ct);Check(users.Count==1&&users[0].Email=="active@sample","Original projected read returns only active user");
 var page=await EfSamples.Page(db,owner,0,200,ct);Check(page.Count==2,"Original pagination executes with capped limit");
 await EfSamples.DeleteArchived(db,ct);
 var rows=await EfSamples.Raw(db,owner,ct);Check(rows.Count==1&&rows[0].Name=="retained","Original ExecuteDelete and parameterized FromSql execute");
 await EfSamples.Save(db,ct);Check(true,"Original generic concurrency-catching fragment compiles/runs no-conflict path only");
}
await using(var db=new ProofDb(options)) {
 counter.Reads=0;var orders=await db.EagerOrders.AsNoTracking().ToListAsync();
 var count=0;foreach(var order in orders)count+=(await db.OrderLines.Where(x=>x.EagerOrderId==order.Id).ToListAsync()).Count;
 Check(counter.Reads==4&&count==6,"Explicit N+1 baseline:3orders/6lines requires4reads");
 counter.Reads=0;var eager=await db.EagerOrders.AsNoTracking().Include(x=>x.Lines).AsSingleQuery().ToListAsync();
 Check(counter.Reads==1&&eager.Count==3&&eager.Sum(x=>x.Lines.Count)==6,"Single-query Include validly eliminates N+1:1read preserves3orders/6lines");
}
var filteredOptions=new DbContextOptionsBuilder<FilterDb>().UseSqlServer(connection).Options;
await using(var db=new FilterDb(filteredOptions,tenant)) {
 var without=await db.Children.AsNoTracking().ToListAsync();
 var query=db.Children.AsNoTracking().Include(x=>x.Order);
 Console.WriteLine("Required-navigation SQL: "+query.ToQueryString());
 var with=await query.ToListAsync();
 Check(without.Count==2&&with.Count==1,"Required filtered navigation INNER JOIN removes child row rather than setting navigation null");
}
Console.WriteLine("DATA PROOF COMPLETED");
