# Concurrency & Synchronization Engineering

Use this reference when coordinating asynchronous work, in-process locking, parallel pipelines, database concurrency, or distributed synchronization in modern .NET.

---

## 1. In-Process Async Synchronization

### Async Mutual Exclusion: `SemaphoreSlim`
Standard `lock` blocks the thread and cannot be used across `await` boundaries. Always use `SemaphoreSlim(1, 1)` with a strict `try/finally` pattern to prevent permanent deadlocks:

```csharp
public sealed class ResourceGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task AccessAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Critical section across await boundaries
            await PerformExclusiveWorkAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
```

### Keyed / Per-Resource Locking
Locking the entire method blocks unrelated requests. When synchronizing operations on a specific entity (e.g. per `customerId` or `orderId`), lock on the entity key:

```csharp
// Use an established keyed lock (e.g., AsyncKeyedLock) or bounded ConcurrentDictionary of Semaphores:
public sealed class KeyedLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task ExecuteAsync(string key, Func<Task> action, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            await action();
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

### Lock-Free Thread Safety: `Interlocked`
For atomic numeric operations and flags, use `Interlocked` instead of locks:

```csharp
private long _processedCount;
private int _isRunning; // 0 = false, 1 = true

public void RecordItem() => Interlocked.Increment(ref _processedCount);

public bool TryStart() => Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;
```

---

## 2. Async Execution & Thread Pool Safety

### Bounded Parallelism: `Parallel.ForEachAsync`
Never use `Parallel.ForEach` over `async` lambdas; it spawns unawaited tasks and overloads the thread pool. Use `Parallel.ForEachAsync` and always set `MaxDegreeOfParallelism`:

```csharp
var options = new ParallelOptions
{
    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8),
    CancellationToken = ct
};

await Parallel.ForEachAsync(items, options, async (item, token) =>
{
    await ProcessItemAsync(item, token);
});
```

### Antipattern: `Task.Run` in ASP.NET Core Request Handlers
❌ **Never wrap asynchronous I/O in `Task.Run` inside an ASP.NET Core request.** It consumes two threads for one operation (one waiting on the task, one executing it) and starves the thread pool under load. Directly `await` the asynchronous method.

### Antipattern: `DbContext` Concurrent Access
❌ **Never share a single `DbContext` instance across multiple tasks or threads.** `DbContext` is not thread-safe. Concurrent access causes `InvalidOperationException` or silent query state corruption. When processing items concurrently, create a separate `IServiceScope` and `DbContext` per task.

---

## 3. Database Concurrency: Optimistic Locking

To prevent lost updates when multiple users edit the same entity concurrently, configure an optimistic concurrency token:

### EF Core Entity Configuration
```csharp
public sealed class Order
{
    public Guid Id { get; init; }
    public decimal TotalAmount { get; set; }

    // Concurrency token updated automatically by database (Postgres xmin / SQL Server rowversion)
    [Timestamp]
    public byte[] Version { get; init; } = [];
}
```

### Handling `DbUpdateConcurrencyException`
```csharp
try
{
    await dbContext.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException ex)
{
    // The record was modified or deleted by another transaction since it was loaded
    var entry = ex.Entries.Single();
    var databaseValues = await entry.GetDatabaseValuesAsync(ct);

    if (databaseValues is null)
    {
        throw new EntityDeletedConflictException("The entity was deleted by another process.");
    }

    throw new ConcurrencyConflictException("The entity was updated by another process. Please refresh and retry.");
}
```

---

## 4. Distributed Concurrency: PostgreSQL Advisory Locks

When synchronizing across multiple application instances (without introducing an external distributed lock service), use PostgreSQL transaction-scoped advisory locks:

```csharp
public sealed class DistributedTaskCoordinator(NpgsqlDataSource dataSource)
{
    public async Task<bool> TryExecuteExclusiveAsync(long lockId, Func<Task> task, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Lock automatically releases when the transaction commits or rolls back
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_xact_lock(@lockId)", connection, transaction);
        command.Parameters.AddWithValue("lockId", lockId);

        var acquired = (bool)(await command.ExecuteScalarAsync(ct) ?? false);
        if (!acquired)
        {
            return false; // Another node is currently executing this task
        }

        await task();
        await transaction.CommitAsync(ct);
        return true;
    }
}
```

---

## 5. Concurrency Review Checklist

- [ ] Every `SemaphoreSlim.WaitAsync()` is immediately followed by a `try ... finally { semaphore.Release(); }`.
- [ ] No `Parallel.ForEach` takes an `async` lambda; `Parallel.ForEachAsync` is used with explicit `MaxDegreeOfParallelism`.
- [ ] `DbContext` is not accessed concurrently by multiple unawaited tasks.
- [ ] Database entities with concurrent write risk have concurrency tokens (`[Timestamp]` or `IsRowVersion()`).
- [ ] Distributed critical sections use transactional advisory locks or database unique constraints.
