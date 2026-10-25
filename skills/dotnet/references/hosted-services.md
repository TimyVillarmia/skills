# Hosted & Background Services

Use this reference when building background workers, scheduled jobs, queue processors, or long-running tasks.

## Core Background Processing Decisions

| Scenario | Do (Recommended) | Never (Anti-pattern) | Verify (How to validate) |
|---|---|---|---|
| **Scoped Dependencies** | Inject `IServiceScopeFactory` into `BackgroundService` and create an explicit scope per iteration/message | Inject a scoped service (e.g. `DbContext`) directly into a singleton `BackgroundService` constructor | Runtime service-provider validation (`ValidateScopes = true`) passes on startup |
| **Periodic Execution** | Use `PeriodicTimer` and await `WaitForNextTickAsync(cancellationToken)` | Use `Thread.Sleep()` or uncontrolled `while(true)` loops | Worker halts promptly when host triggers shutdown cancellation |
| **Queue Processing** | Use bounded `Channel<T>` to decouple producer intake from consumer background execution | Spawn unconstrained `Task.Run()` or unbounded in-memory lists (memory leak) | Queue honors backpressure when channel reaches `BoundedChannelOptions.Capacity` |
| **Graceful Shutdown** | Pass the `stoppingToken` from `ExecuteAsync` to every async call and configure `ShutdownTimeout` | Swallow `OperationCanceledException` and keep executing work after cancellation | Worker completes current unit of work or aborts within configured shutdown window |
| **Transient Retries** | Use exponential backoff with jitter and a bounded maximum retry count | Retry in a tight loop without delay or retry non-transient domain errors indefinitely | Failed iterations log error with backoff delay, then retry or route to dead-letter queue |

---

## Scoped Dependency Lifetime Pattern

`BackgroundService` is registered as a Singleton. To consume scoped services (like EF Core `DbContext` or use-case handlers), create an explicit scope for each processing cycle:

```csharp
public sealed class OrderProcessingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Create an isolated scope for the iteration
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ProcessPendingOrdersHandler>();
                await handler.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Order processing worker is stopping gracefully.");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error during order processing iteration.");
            }
        }
    }
}
```

---

## Producer-Consumer with Bounded Channels

Use `System.Threading.Channels.Channel<T>` to buffer incoming work and enforce backpressure:

```csharp
public sealed class WorkQueue<T>
{
    private readonly Channel<T> _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(T item, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(item, ct);

    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);
}
```

### Channel Consumer Worker

```csharp
public sealed class QueueProcessorWorker(
    WorkQueue<WorkItem> queue,
    IServiceScopeFactory scopeFactory,
    ILogger<QueueProcessorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IItemProcessor>();
                await processor.ProcessAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing queue item {ItemId}.", item.Id);
            }
        }
    }
}
```

---

## Graceful Shutdown Configuration

By default, .NET allows 30 seconds for hosted services to stop after SIGTERM. Adjust the shutdown timeout to accommodate in-flight batch operations:

```csharp
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(15);
});
```

Key rules:
- Always check `stoppingToken.IsCancellationRequested` before starting a new batch.
- Pass `stoppingToken` to all async I/O operations (`ReadAsync`, `SaveChangesAsync`, HTTP calls).
- Catch `OperationCanceledException` explicitly; do not treat normal cancellation as an unhandled error.
