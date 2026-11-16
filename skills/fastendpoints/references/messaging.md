# Messaging, Events & Background Jobs in FastEndpoints

FastEndpoints includes built-in messaging and event abstractions, eliminating external mediator libraries (such as MediatR) for in-process command dispatch and pub/sub.

---

## 1. The In-Process Command Bus

Use the Command Bus to decouple HTTP endpoint handling from core business logic or to reuse workflows across multiple endpoints:

### Defining a Command & Handler
```csharp
using FastEndpoints;

// 1. The Command contract
public sealed class SendWelcomeEmailCommand : ICommand<bool>
{
    public required string RecipientEmail { get; init; }
    public required string UserName { get; init; }
}

// 2. The Command Handler
public sealed class SendWelcomeEmailHandler : ICommandHandler<SendWelcomeEmailCommand, bool>
{
    private readonly IEmailService _emailService;

    public SendWelcomeEmailHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task<bool> ExecuteAsync(SendWelcomeEmailCommand command, CancellationToken ct)
    {
        return await _emailService.SendAsync(command.RecipientEmail, command.UserName, ct);
    }
}
```

### Executing Commands in Endpoints
Handlers are automatically discovered and registered in DI. Execute commands directly on the command instance:

```csharp
public override async Task HandleAsync(RegisterRequest req, CancellationToken ct)
{
    // Execute command via extension method
    bool sent = await new SendWelcomeEmailCommand
    {
        RecipientEmail = req.Email,
        UserName = req.Name
    }.ExecuteAsync(ct);

    await SendOkAsync(new RegisterResponse { EmailSent = sent }, ct);
}
```

---

## 2. In-Process Event Bus (Pub / Sub)

Use the Event Bus to publish domain events to one or many decoupled subscribers.

### Defining an Event & Handler
```csharp
// 1. Define event record
public sealed record OrderPlacedEvent(string OrderId, decimal TotalAmount) : IEvent;

// 2. Handler A: Update inventory
public sealed class UpdateInventoryOnOrderPlaced : IEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent eventModel, CancellationToken ct)
    {
        // Adjust inventory levels
    }
}

// 3. Handler B: Notify accounting
public sealed class NotifyAccountingOnOrderPlaced : IEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent eventModel, CancellationToken ct)
    {
        // Record ledger entry
    }
}
```

### Publishing Events
Publish events with execution strategy:
```csharp
var orderEvent = new OrderPlacedEvent(order.Id, order.Total);

// WaitForAll: awaits all handlers in parallel
await orderEvent.PublishAsync(Mode.WaitForAll, ct);

// WaitForAny: returns as soon as the first handler completes
// await orderEvent.PublishAsync(Mode.WaitForAny, ct);

// None: fire and forget in background
// await orderEvent.PublishAsync(Mode.None, ct);
```

---

## 3. Background Job Queues

FastEndpoints provides persistent background job queues without needing Hangfire or Quartz for simple task queues:

```csharp
// 1. Define job payload
public sealed class ProcessVideoJob : IJob
{
    public required string VideoId { get; init; }
}

// 2. Define job handler
public sealed class ProcessVideoJobHandler : IJobHandler<ProcessVideoJob>
{
    public async Task ExecuteAsync(ProcessVideoJob job, CancellationToken ct)
    {
        // Transcode video
    }
}

// 3. Enqueue from an endpoint:
await new ProcessVideoJob { VideoId = "vid-123" }.QueueJobAsync(ct);
```

---

## 4. Server-Sent Events (SSE)

Stream real-time updates over standard HTTP without WebSocket overhead:

```csharp
public sealed class LiveTickerEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/live-ticker");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendEventStreamAsync("price-stream", GetPriceUpdatesAsync(ct), ct);
    }

    private async IAsyncEnumerable<PriceUpdate> GetPriceUpdatesAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return new PriceUpdate(DateTime.UtcNow, Random.Shared.Next(100, 200));
            await Task.Delay(1000, ct);
        }
    }
}
```
