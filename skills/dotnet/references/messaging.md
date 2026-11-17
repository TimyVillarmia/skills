# Distributed Messaging & Transactional Outbox

Use this reference when building event-driven systems, asynchronous messaging pipelines, or background integrations across service boundaries in modern .NET.

## Contents

- [1. The Dual-Write Problem](#1-the-dual-write-problem)
- [2. The Solution: Transactional Outbox Pattern](#2-the-solution-transactional-outbox-pattern)
- [3. At-Least-Once Delivery & Idempotent Consumers (The Inbox)](#3-at-least-once-delivery--idempotent-consumers-the-inbox)
- [4. Consumer Resilience & Dead-Letter Queues (DLQ)](#4-consumer-resilience--dead-letter-queues-dlq)
- [5. Integration Testing Distributed Messaging](#5-integration-testing-distributed-messaging)
- [6. Legacy / Existing Codebases: MassTransit v8.x (Pinned)](#6-legacy--existing-codebases-masstransit-v8x-pinned)
- [7. Distributed Messaging Checklist](#7-distributed-messaging-checklist)

---

## 1. The Dual-Write Problem

❌ **Antipattern:** Never commit a database transaction and publish a message to a broker in two independent calls:

```csharp
// DANGEROUS: If network drops between these two lines, DB commits but message is lost forever.
await _dbContext.SaveChangesAsync(ct);
await _messageBus.PublishAsync(new OrderCreatedEvent(order.Id), ct);
```

If the database commit succeeds but message publishing fails, downstream services never receive the event. If you publish first and the database commit rolls back, downstream services process a ghost entity.

---

## 2. The Solution: Transactional Outbox Pattern

Persist the message in an outbox table **inside the exact same database transaction** as your business data. A background worker then polls the outbox table, publishes to the message broker, and marks the record as published.

```text
HTTP Request -> DB Transaction (Atomic)
                ├── INSERT INTO Orders (...)
                └── INSERT INTO OutboxMessages (...)
                     │
                     ▼
Background Worker -> Read Unpublished Messages -> Publish to Broker -> Mark Processed
```

### Selecting an Outbox Implementation

Follow the dependency decision order in `SKILL.md`:

| Scenario | Recommended Default | License |
|---|---|---|
| Single service, simple broker integration, or strict Native AOT | **Option A: Native EF Core Outbox** (`DbContext` + `PeriodicTimer`) | BCL / MIT / Apache-2.0 |
| Multi-service distributed bus, sagas, retries, and turnkey Inbox/Outbox | **Option B: Wolverine (`WolverineFx.EntityFrameworkCore`)** | MIT |
| Existing codebase already using MassTransit without a commercial v9 license | **Option C: Legacy MassTransit v8.x** (strictly pinned `< 9.0.0`) | Apache-2.0 (v8.x only; v9+ is commercial) |

---

### Option A: Native EF Core Outbox (`DbContext` + `PeriodicTimer`)

Use a small explicit outbox implementation when you want zero framework lock-in, full Native AOT compatibility, or simple point-to-point/pub-sub publishing.

#### 1. Outbox Entity & Atomic Write
Store serialized event payloads alongside domain mutations in the same `SaveChangesAsync` call:

```csharp
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7(); // .NET 9+
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public required DateTimeOffset OccurredOnUtc { get; init; }
    public DateTimeOffset? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
    public Guid? ClaimId { get; set; }
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }
}

[JsonSerializable(typeof(OrderCreatedEvent))]
internal partial class AppJsonContext : JsonSerializerContext
{
}

// Inside endpoint or handler — single atomic transaction
public async Task HandleAsync(CreateOrderRequest req, AppDbContext db, TimeProvider timeProvider, CancellationToken ct)
{
    var order = new Order { Id = Guid.CreateVersion7(), CustomerId = req.CustomerId };
    db.Orders.Add(order);

    var evt = new OrderCreatedEvent(order.Id);
    db.OutboxMessages.Add(new OutboxMessage
    {
        Type = typeof(OrderCreatedEvent).FullName!,
        Payload = JsonSerializer.Serialize(evt, AppJsonContext.Default.OrderCreatedEvent),
        OccurredOnUtc = timeProvider.GetUtcNow()
    });

    await db.SaveChangesAsync(ct);
}
```

#### 2. Background Sweeper (`BackgroundService` + `PeriodicTimer`)
Resolve `AppDbContext` inside a fresh `IServiceScope` per tick. Claim each candidate with an atomic conditional update and a short lease so concurrent service instances do not process the same row. Set the lease longer than expected batch work or renew it for long-running publishes. In production, also track attempt counts with backoff and move poison messages to a dead-letter state after max attempts. A crash after broker publication but before the processed update can still cause a retry; consumers must remain idempotent.

```csharp
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IMessageBrokerPublisher>();
                var now = timeProvider.GetUtcNow();
                var claimId = Guid.NewGuid();

                var candidates = await db.OutboxMessages
                    .AsNoTracking()
                    .Where(m => m.ProcessedOnUtc == null &&
                        (m.ClaimExpiresAtUtc == null || m.ClaimExpiresAtUtc <= now))
                    .OrderBy(m => m.OccurredOnUtc)
                    .Select(m => m.Id)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                if (candidates.Count == 0)
                {
                    continue;
                }

                await db.OutboxMessages
                    .Where(m => candidates.Contains(m.Id) && m.ProcessedOnUtc == null &&
                        (m.ClaimExpiresAtUtc == null || m.ClaimExpiresAtUtc <= now))
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(m => m.ClaimId, claimId)
                        .SetProperty(m => m.ClaimExpiresAtUtc, now.AddMinutes(5)), stoppingToken);

                var messages = await db.OutboxMessages
                    .Where(m => m.ClaimId == claimId)
                    .OrderBy(m => m.OccurredOnUtc)
                    .ToListAsync(stoppingToken);

                foreach (var message in messages)
                {
                    try
                    {
                        await publisher.PublishRawAsync(message.Id, message.Type, message.Payload, stoppingToken);
                        message.ProcessedOnUtc = timeProvider.GetUtcNow();
                        message.Error = null;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        message.Error = ex.Message;
                        message.ClaimId = null;
                        message.ClaimExpiresAtUtc = timeProvider.GetUtcNow().AddSeconds(30); // Backoff before retry
                        logger.LogError(ex, "Failed publishing outbox message {OutboxMessageId}.", message.Id);
                    }
                    finally
                    {
                        if (message.ProcessedOnUtc is not null)
                        {
                            message.ClaimId = null;
                            message.ClaimExpiresAtUtc = null;
                        }
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing outbox batch.");
            }
        }
    }
}
```

---

### Option B: Wolverine EF Core Outbox (`WolverineFx`, MIT — Recommended Bus Default)

When a full distributed message bus is warranted (automatic topology provisioning, transactional inbox/outbox, sagas, dead-letter queues), use **Wolverine** (`WolverineFx.EntityFrameworkCore` + `WolverineFx.Postgresql` or `WolverineFx.SqlServer`, plus `WolverineFx.RuntimeCompilation` for dynamic codegen or `TypeLoadMode.Static` for pre-generated code; [Wolverine Documentation](https://wolverinefx.net/guide/durability/efcore/outbox-and-inbox)).

#### 1. Service & Durability Registration (`Program.cs`)
```csharp
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

builder.Host.UseWolverine(opts =>
{
    // Wolverine 6+ requires either WolverineFx.RuntimeCompilation or TypeLoadMode.Static
    // Persist inbox/outbox envelopes in PostgreSQL (or PersistMessagesWithSqlServer)
    opts.PersistMessagesWithPostgresql(builder.Configuration.GetConnectionString("DefaultConnection")!);
    opts.UseEntityFrameworkCoreTransactions();

    // Configure broker transport and auto-provision queues/exchanges
    opts.UseRabbitMq(rabbit => rabbit.HostName = "localhost")
        .AutoProvision();

    // Enforce durable outbox and inbox across endpoints
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    opts.Policies.UseDurableInboxOnAllListeners();
});
```

#### 2. Publishing via `IDbContextOutbox<TDbContext>`
Outside Wolverine handlers (e.g., Minimal API endpoints), inject `IDbContextOutbox<AppDbContext>` to enroll EF Core changes and outgoing messages in the same atomic transaction:

```csharp
public static async Task<Ok<OrderDto>> CreateOrder(
    CreateOrderRequest req,
    IDbContextOutbox<AppDbContext> outbox,
    CancellationToken ct)
{
    var order = new Order { Id = Guid.CreateVersion7(), CustomerId = req.CustomerId };
    outbox.DbContext.Orders.Add(order);

    // Queues message in the outbox; flushed after DB transaction commits
    await outbox.PublishAsync(new OrderCreatedEvent(order.Id));

    // Atomically commits Orders + Outbox envelope and triggers background flush
    await outbox.SaveChangesAndFlushMessagesAsync(ct);

    return TypedResults.Ok(OrderDto.From(order));
}
```

---

## 3. At-Least-Once Delivery & Idempotent Consumers (The Inbox)

Message brokers guarantee **at-least-once** delivery, not exactly-once. Network timeouts or consumer crashes during acknowledgments cause brokers to re-deliver the same message.

**Consumers must be idempotent.**

### Idempotency Strategies:
1. **Durable Inbox (Automatic)**:
   - **Wolverine**: Calling `opts.Policies.UseDurableInboxOnAllListeners()` records incoming envelope IDs in the durable inbox table and suppresses duplicates automatically.
   - **Native EF Core**: Record processed `MessageId` values in an `InboxMessages` table with a primary/unique key inside the consumer's `DbContext` transaction.
2. **Database Unique Constraints**:
   Use natural database keys (e.g. `UNIQUE (PaymentId, TransactionId)`) so duplicate processing fails safely without corrupting data.
3. **Optimistic State Guards**:
   Check entity state before processing (e.g. `if (order.IsProcessed) return;`).

```csharp
// Wolverine convention-based handler (no framework interface required)
public static class OrderCreatedHandler
{
    public static async Task Handle(
        OrderCreatedEvent message,
        AppDbContext db,
        CancellationToken ct)
    {
        var existingOrder = await db.Orders.FindAsync([message.OrderId], ct);
        if (existingOrder is null || existingOrder.IsProcessed)
        {
            return;
        }

        existingOrder.IsProcessed = true;
        await db.SaveChangesAsync(ct);
    }
}
```

---

## 4. Consumer Resilience & Dead-Letter Queues (DLQ)

Transient errors (network blips, database timeouts, `DbUpdateConcurrencyException`) require retries with backoff. Permanent errors (invalid payload, business rule violation) must route to a Dead-Letter Queue (or error table) to prevent blocking the stream.

```csharp
// Wolverine failure policies inside UseWolverine(...)
opts.OnException<NpgsqlException>()
    .Or<DbUpdateConcurrencyException>()
    .RetryWithCooldown(
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2))
    .Then.MoveToErrorQueue();
```

---

## 5. Integration Testing Distributed Messaging

Verify message publication and consumer execution in integration tests using Wolverine's `ITrackedSession` (`InvokeMessageAndWaitAsync` / `ExecuteAndWaitAsync`):

```csharp
[TestMethod]
public async Task OrderCreated_PublishesAndConsumesEvent()
{
    // 1. Arrange host (requires WolverineFx.RuntimeCompilation or TypeLoadMode.Static)
    using var host = await Host.CreateDefaultBuilder()
        .UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(OrderCreatedHandler).Assembly);
        })
        .StartAsync();

    // 2. Act: Execute and wait for all cascading/published messages to complete
    ITrackedSession session = await host.InvokeMessageAndWaitAsync(new OrderCreatedEvent(Guid.NewGuid()));

    // 3. Assert: Verify message was processed without exceptions
    Assert.IsNotNull(session);
    Assert.AreEqual(1, session.Executed.MessagesOf<OrderCreatedEvent>().Count());
}
```

---

## 6. Legacy / Existing Codebases: MassTransit v8.x (Pinned)

> ⚠️ **Commercial License Trap (`MassTransit >= 9.0`):** MassTransit v9+ requires a paid commercial license ([MassTransit License Configuration](https://masstransit.massient.com/configuration/license)). Do **not** introduce MassTransit in greenfield projects or upgrade existing projects to `9.x` without explicit commercial license approval. In existing v8 codebases, strictly pin packages to `< 9.0.0` (e.g., `Version="[8.3.*,9.0.0)"` in `Directory.Packages.props`).

When maintaining an existing MassTransit v8 codebase:

### 1. DbContext Entity Registration
Register MassTransit's inbox and outbox entities in `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.AddInboxStateEntity();
    modelBuilder.AddOutboxMessageEntity();
    modelBuilder.AddOutboxStateEntity();
}
```

### 2. Bus & Consumer Outbox Registration
Attach `UseEntityFrameworkOutbox<AppDbContext>(context)` on receive endpoints via `AddConfigureEndpointsCallback` so the Inbox deduplicates incoming messages within `DuplicateDetectionWindow` (bus outbox registration alone does not protect consumers):

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();

    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
    });

    x.AddConfigureEndpointsCallback((context, name, endpoint) =>
    {
        endpoint.UseEntityFrameworkOutbox<AppDbContext>(context);
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        cfg.ConfigureEndpoints(context);
    });
});
```

### 3. Testing with `ITestHarness`
In MassTransit v8 projects, use `AddMassTransitTestHarness` and `ITestHarness` (`await harness.Consumed.Any<T>()`) to verify consumer execution in memory.

---

## 7. Distributed Messaging Checklist

- [ ] Does business data mutation + event publication use the Transactional Outbox pattern?
- [ ] Are greenfield projects using permissive defaults (`Native EF Core Outbox` or `WolverineFx` MIT) rather than unpinned commercial libraries?
- [ ] If maintaining an existing MassTransit codebase without a commercial license, are all `MassTransit*` packages pinned strictly below `9.0.0`?
- [ ] Are consumers idempotent against duplicate message deliveries (Inbox table / natural unique keys / state guards)?
- [ ] Is message retry configured with backoff/cooldown for transient failures and dead-letter routing for poison messages?
- [ ] Do integration tests verify end-to-end publishing and consumer handling?
