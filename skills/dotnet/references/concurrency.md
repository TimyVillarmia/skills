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
Locking the entire method blocks unrelated requests. When synchronizing operations on a specific entity (e.g. per `customerId` or `orderId`), lock on the entity key.

> [!WARNING]
> **Keyed Lock Dictionary Memory Leak & Race Conditions:**
> Storing raw `SemaphoreSlim` in a `ConcurrentDictionary<string, SemaphoreSlim>` without reference-counted eviction leaks memory indefinitely as new keys accumulate.
> Naively removing semaphores in `finally` causes concurrency races where a waiter holds an evicted instance while a new thread instantiates another, breaking mutual exclusion.
> In production, use a battle-tested reference-counting library like **`AsyncKeyedLock`**:

```csharp
// Using AsyncKeyedLock (NuGet: AsyncKeyedLock)
public sealed class OrderProcessingService(AsyncKeyedLocker<string> locker)
{
    public async Task ProcessOrderAsync(string orderId, CancellationToken ct)
    {
        // Automatically manages reference count and safely disposes/evicts semaphore when waiters reach 0
        using (await locker.LockAsync(orderId, ct).ConfigureAwait(false))
        {
            await ExecuteOrderCriticalSectionAsync(orderId, ct);
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

To prevent lost updates when multiple users edit the same entity concurrently, configure an optimistic concurrency token.

### Provider-Specific Concurrency Tokens

- **SQL Server (`rowversion` / `byte[]`):**
  Uses a hidden 8-byte binary counter auto-incremented by the engine on every row update:
  ```csharp
  public sealed class Order
  {
      public Guid Id { get; init; }
      public decimal TotalAmount { get; set; }

      [Timestamp]
      public byte[] RowVersion { get; init; } = [];
  }
  ```

- **PostgreSQL (`xmin` / `uint`):**
  PostgreSQL uses the internal transaction ID system column `xmin` (unsigned 32-bit integer). Do not use `byte[]`:
  ```csharp
  public sealed class Order
  {
      public Guid Id { get; init; }
      public decimal TotalAmount { get; set; }

      // Mapped to Postgres system column 'xmin'
      [Timestamp]
      public uint Version { get; init; }
  }

  // In DbContext OnModelCreating:
  modelBuilder.Entity<Order>().UseXminAsConcurrencyToken();
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
    public async Task<bool> TryExecuteExclusiveAsync(long lockId, Func<CancellationToken, Task> task, CancellationToken ct)
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

        await task(ct);
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
