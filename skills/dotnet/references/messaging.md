# Distributed Messaging & Transactional Outbox

Use this reference when building event-driven systems, asynchronous messaging pipelines, or background integrations across service boundaries in modern .NET.

---

## 1. The Dual-Write Problem

❌ **Antipattern:** Never commit a database transaction and publish a message to a broker in two independent calls:

```csharp
// DANGEROUS: If network drops between these two lines, DB commits but message is lost forever.
await _dbContext.SaveChangesAsync(ct);
await _publishEndpoint.Publish(new OrderCreatedEvent(order.Id), ct);
```

If the database commit succeeds but message publishing fails, downstream services never receive the event. If you publish first and the database commit rolls back, downstream services process a ghost entity.

---

## 2. The Solution: Transactional Outbox Pattern

Persist the message in an `OutboxMessages` table **inside the exact same database transaction** as your business data. A background worker then polls the outbox table, publishes to the message broker, and marks the record as published.

```text
HTTP Request -> DB Transaction (Atomic)
                ├── INSERT INTO Orders (...)
                └── INSERT INTO OutboxMessages (...)
                     │
                     ▼
Background Worker -> Read Unpublished Messages -> Publish to Broker -> Mark Processed
```

### MassTransit EF Core Outbox (Recommended Standard)

Do not write custom outbox tables and polling workers. Use MassTransit's battle-tested EF Core Outbox:

```csharp
// Program.cs
builder.Services.AddMassTransit(x =>
{
    // Register consumers
    x.AddConsumer<OrderCreatedConsumer>();

    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        // Polls the outbox table every 1 second and publishes via RabbitMQ/Azure Service Bus
        o.UsePostgres(); // or UseSqlServer()
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h => { h.Username("guest"); h.Password("guest"); });
        cfg.ConfigureEndpoints(context);
    });
});
```

### Publishing via the Outbox
Inject `IPublishEndpoint`. When the outbox is registered, `Publish()` writes to the local DbContext transaction instead of making immediate network calls:

```csharp
public async Task HandleAsync(CreateOrderRequest req, CancellationToken ct)
{
    var order = new Order { Id = Guid.NewGuid(), CustomerId = req.CustomerId };
    _dbContext.Orders.Add(order);

    // Written to the outbox table in memory; committed atomically on SaveChangesAsync
    await _publishEndpoint.Publish(new OrderCreatedEvent(order.Id), ct);

    await _dbContext.SaveChangesAsync(ct);
}
```

---

## 3. At-Least-Once Delivery & Idempotent Consumers (The Inbox)

Message brokers guarantee **at-least-once** delivery, not exactly-once. Network timeouts or consumer crashes during acknowledgments cause brokers to re-deliver the same message.

**Consumers must be idempotent.**

### Idempotency Strategies:
1. **MassTransit Inbox (Automatic)**:
   When `AddEntityFrameworkOutbox` is configured, MassTransit records incoming message IDs in an `InboxState` table. If a duplicate message arrives, the consumer execution is skipped automatically.
2. **Database Unique Constraints**:
   Use natural database keys (e.g. `UNIQUE (PaymentId, TransactionId)`) so duplicate processing fails safely without corrupting data.
3. **Optimistic Locking**:
   Check entity state before processing (e.g. `if (order.Status == OrderStatus.Paid) return;`).

```csharp
public sealed class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly AppDbContext _db;

    public OrderCreatedConsumer(AppDbContext db) => _db = db;

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var message = context.Message;

        // Idempotency check: Guard against duplicate processing
        var existingOrder = await _db.Orders.FindAsync(message.OrderId);
        if (existingOrder is null || existingOrder.IsProcessed)
        {
            return;
        }

        existingOrder.IsProcessed = true;
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
```

---

## 4. Consumer Resilience & Dead-Letter Queues (DLQ)

Transient errors (network blips, database timeouts) require retries. Permanent errors (invalid JSON, missing data) must route to a Dead-Letter Queue (Error queue) to prevent blocking the message stream.

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    // Exponential backoff with jitter for transient failures
    cfg.UseMessageRetry(r => r.Exponential(
        retryLimit: 5,
        minInterval: TimeSpan.FromSeconds(1),
        maxInterval: TimeSpan.FromSeconds(30),
        intervalDelta: TimeSpan.FromSeconds(2)));

    // Configure dead-letter / error queue routing automatically
    cfg.ConfigureEndpoints(context);
});
```

---

## 5. Integration Testing with MassTransit Test Harness

Use `MassTransit.Testing.ITestHarness` to verify message publishing and consumer execution in memory without requiring a running broker in unit/integration tests:

```csharp
[TestMethod]
public async Task OrderCreated_PublishesAndConsumesEvent()
{
    // 1. Arrange test harness
    await using var provider = new ServiceCollection()
        .AddMassTransitTestHarness(x => x.AddConsumer<OrderCreatedConsumer>())
        .BuildServiceProvider(true);

    var harness = provider.GetRequiredService<ITestHarness>();
    await harness.Start();

    // 2. Act
    await harness.Bus.Publish(new OrderCreatedEvent(Guid.NewGuid()));

    // 3. Assert: Verify consumer executed
    Assert.IsTrue(await harness.Consumed.Any<OrderCreatedEvent>());
    var consumerHarness = harness.GetConsumerHarness<OrderCreatedConsumer>();
    Assert.IsTrue(await consumerHarness.Consumed.Any<OrderCreatedEvent>());
}
```

---

## 6. Distributed Messaging Checklist

- [ ] Does business data mutation + event publication use the Transactional Outbox pattern?
- [ ] Are consumers idempotent against duplicate message deliveries (Inbox / natural keys)?
- [ ] Is message retry configured with exponential backoff and jitter?
- [ ] Are unrecoverable messages routed to a dead-letter/error queue after retries exhaust?
- [ ] Do integration tests verify publishing and consumption using `ITestHarness`?
