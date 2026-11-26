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

## Scoped Lifetime & `PeriodicTimer` Pattern

`BackgroundService` is a Singleton. Create an explicit `AsyncServiceScope` per iteration and pass `TimeProvider` to `PeriodicTimer` for deterministic testing:

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider);

while (!stoppingToken.IsCancellationRequested &&
       await timer.WaitForNextTickAsync(stoppingToken))
{
    try
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProcessPendingOrdersHandler>();
        await handler.ExecuteAsync(stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
    catch (Exception ex) { logger.LogError(ex, "Unhandled error during background iteration."); }
}
```

## Bounded Channels (Volatile In-Process Work)

Use bounded `Channel<T>` to enforce backpressure for volatile in-process work (for durable work that must survive restarts or route to a DLQ, use the outbox/broker patterns in [messaging.md](messaging.md); wrap transient processing failures in a `ResiliencePipeline`):

```csharp
private readonly Channel<WorkItem> _channel = Channel.CreateBounded<WorkItem>(
    new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
```

## Graceful Shutdown Configuration

By default, .NET allows 30 seconds for hosted services to stop after `SIGTERM`. Configure `HostOptions.ShutdownTimeout` to match in-flight batch bounds:

```csharp
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(15);
});
```

- Check `stoppingToken.IsCancellationRequested` before starting a new batch.
- Pass `stoppingToken` to all async I/O (`ReadAsync`, `SaveChangesAsync`, HTTP calls).
- Handle `OperationCanceledException` as expected shutdown when `stoppingToken.IsCancellationRequested` is true rather than logging normal cancellation as an unhandled error.
