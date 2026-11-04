using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Npgsql;
using Microsoft.EntityFrameworkCore;
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

    public Func<CancellationToken, Task> Work { get; set; } = _ => Task.CompletedTask;
    private Task PerformExclusiveWorkAsync(CancellationToken ct) => Work(ct);

    public void Dispose() => _gate.Dispose();
}
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
public sealed class Order
{
    public Guid Id { get; init; }
    public decimal TotalAmount { get; set; }

    // Concurrency token updated automatically by database (Postgres xmin / SQL Server rowversion)
    [Timestamp]
    public byte[] Version { get; init; } = [];
}
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

public sealed class AtomicState {
private long _processedCount;
private int _isRunning; // 0 = false, 1 = true

public void RecordItem() => Interlocked.Increment(ref _processedCount);

public bool TryStart() => Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;

 public long Processed => Interlocked.Read(ref _processedCount);
}
public static class ParallelSample {
 public static async Task Run(CancellationToken ct) {
 var items = Enumerable.Range(1,8);
var options = new ParallelOptions
{
    MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8),
    CancellationToken = ct
};

await Parallel.ForEachAsync(items, options, async (item, token) =>
{
    await ProcessItemAsync(item, token);
});

 }
 private static Task ProcessItemAsync(int item,CancellationToken ct) => Task.CompletedTask;
}
public sealed class EntityDeletedConflictException(string message):Exception(message);
public sealed class ConcurrencyConflictException(string message):Exception(message);
public static class ConflictSample {
 public static async Task Save(ProofDb dbContext,CancellationToken ct) {
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

 }
}
