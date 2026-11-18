# Resilience & Fault Tolerance

Use this reference when configuring outbound HTTP resilience, non-HTTP `ResiliencePipeline` strategies, inbound `UseRequestTimeouts`, retry/timeout budgets, circuit breakers, or upstream `429` handling.

## Contents

- [Outbound HTTP resilience](#outbound-http-resilience)
- [Non-HTTP ResiliencePipeline](#non-http-resiliencepipeline)
- [Retry safety and EF Core trap](#retry-safety-and-ef-core-trap)
- [Timeout budgets and circuit breakers](#timeout-budgets-and-circuit-breakers)
- [Inbound Request Timeouts middleware](#inbound-request-timeouts-middleware)
- [Honoring upstream 429 and Retry-After](#honoring-upstream-429-and-retry-after)
- [Partial failures and fallbacks](#partial-failures-and-fallbacks)
- [Resilience anti-patterns](#resilience-anti-patterns)

---

## Outbound HTTP resilience

For outbound HTTP communication, use `Microsoft.Extensions.Http.Resilience` (`AddStandardResilienceHandler`):

```csharp
builder.Services.AddHttpClient<InventoryClient>(client =>
{
    client.BaseAddress = new Uri("https://inventory.service.local");
}).AddStandardResilienceHandler(options =>
{
    options.Retry.DisableForUnsafeHttpMethods();
});
```

The standard resilience handler executes strategies in this order (outermost to innermost):

1. **Rate Limiter** (sheds load immediately before exhausting timeouts or retry budgets)
2. **Total Request Timeout** (overall deadline across all attempts)
3. **Standard Retry** (exponential backoff with jitter on transient failures)
4. **Circuit Breaker** (fails fast when failure rate exceeds threshold)
5. **Attempt Timeout** (timeout per individual HTTP request attempt)

> [!NOTE]
> `Microsoft.Extensions.Http.Resilience` and `Microsoft.Extensions.Resilience` are published under Microsoft's standard package license. Direct Polly NuGet binaries published after November 16, 2026 carry the OSMF commercial EULA (pre-Nov-16 v8.x binaries remain BSD-3-Clause); transitive usage via Microsoft's resilience packages remains covered by Microsoft's license.

---

## Non-HTTP `ResiliencePipeline`

For non-HTTP transient calls (message brokers, external SDKs, storage blobs, background-job steps), register keyed pipelines via `Microsoft.Extensions.Resilience` (`AddResiliencePipeline`) and resolve them through `ResiliencePipelineProvider<string>`:

```csharp
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

builder.Services.AddResiliencePipeline("broker-publish", static builder =>
{
    builder
        .AddTimeout(TimeSpan.FromSeconds(10)) // Total timeout
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(200),
            ShouldHandle = new PredicateBuilder().Handle<TimeoutException>().Handle<BrokerTransientException>()
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 10,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15)
        })
        .AddTimeout(TimeSpan.FromSeconds(3)); // Per-attempt timeout
});

public sealed class BrokerPublisher(ResiliencePipelineProvider<string> pipelineProvider)
{
    private readonly ResiliencePipeline _pipeline = pipelineProvider.GetPipeline("broker-publish");

    public ValueTask PublishAsync(OrderMessage message, CancellationToken ct) =>
        _pipeline.ExecuteAsync(async token => await SendInternalAsync(message, token), ct);
}
```

Use `AddResiliencePipeline<string, TResult>(...)` when the pipeline must inspect or handle return values (e.g., transient result codes) rather than exceptions alone.

---

## Retry safety and EF Core trap

- **Retry only idempotent operations:** Disable retries for unsafe HTTP methods (`POST`, `PATCH`, `PUT`, `DELETE`, `CONNECT`) unless the endpoint contract guarantees idempotency or enforces an `Idempotency-Key` (see [HTTP Idempotency & Concurrency](api.md#idempotency--concurrency)).
- **Always use exponential backoff with jitter:** Fixed or tight retry loops synchronize callers and amplify thundering herds against recovering dependencies.
- **Do not wrap `DbContext.SaveChangesAsync()` in a generic `ResiliencePipeline`:** Retrying `SaveChangesAsync()` on the same `DbContext` instance leaves failed, partially mutated tracked entities in `ChangeTracker` and risks corrupting state or duplicating inserts. Use EF Core's built-in execution strategy (`EnableRetryOnFailure()` / `db.Database.CreateExecutionStrategy()`) or create a fresh `IServiceScope` and `DbContext` per attempt.

---

## Timeout budgets and circuit breakers

Keep timeout hierarchies mathematically consistent so outer deadlines do not prematurely abort valid retries or allow retries to exceed caller SLAs:

`TotalRequestTimeout > AttemptTimeout * (MaxRetryAttempts + 1) + TotalBackoff`

- Set **Attempt Timeout** tight enough to fail hung sockets quickly.
- Set **Total Request Timeout** to the caller's SLA budget.
- Configure **Circuit Breaker** `MinimumThroughput` and `SamplingDuration` high enough to avoid tripping on a handful of isolated errors during low traffic.

---

## Inbound Request Timeouts middleware

Outbound resilience governs calls your service makes; ASP.NET Core's `AddRequestTimeouts` / `UseRequestTimeouts` enforces server-side deadlines on **incoming** HTTP requests:

```csharp
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(15),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
    };
    options.AddPolicy("export", TimeSpan.FromMinutes(2));
});

// Place AFTER UseRouting (when explicit) and BEFORE UseOutputCache / endpoints
app.UseRequestTimeouts();

app.MapGet("/api/reports/export", ExportReport).WithRequestTimeout("export");
app.MapGet("/api/events/stream", StreamEvents).DisableRequestTimeout();
```

- **Cooperative cancellation only:** The middleware cancels `HttpContext.RequestAborted` (`CancellationToken`). Endpoints, EF Core queries, and outbound `HttpClient` calls must propagate `ct` or work continues in the background.
- **Disabled under debugger:** Request timeouts do not fire when a debugger is attached (`Debugger.IsAttached`); verify timeout behavior without a debugger attached or via integration tests.
- **Started responses & streams:** Once response headers/bytes have flushed (e.g., SSE or file streaming), the middleware cannot rewrite the status code to `504`—apply `.DisableRequestTimeout()` on long-lived streaming endpoints.

---

## Honoring upstream `429` and `Retry-After`

When calling external APIs that return `429 Too Many Requests` or `503 Service Unavailable` with a `Retry-After` header, respect the server's backoff hint, and decline the retry in `ShouldHandle` when the requested delay exceeds the caller's SLA budget so an upstream `Retry-After: 3600` does not pin request threads or trigger early retries:

```csharp
var maxRetryDelay = TimeSpan.FromSeconds(15);

// ShouldRetryAfterHeader is true by default in Microsoft.Extensions.Http.Resilience
// and automatically honors Retry-After delays. Override ShouldHandle only to fail fast
// when Retry-After exceeds the caller's SLA budget:
options.Retry.ShouldHandle = args =>
{
    if (!HttpClientResiliencePredicates.IsTransient(args.Outcome))
    {
        return ValueTask.FromResult(false);
    }

    var retryAfter = args.Outcome.Result?.Headers.RetryAfter;
    var delay = retryAfter?.Delta
        ?? (retryAfter?.Date is DateTimeOffset date ? date - TimeProvider.System.GetUtcNow() : null);

    // Fail fast instead of waiting longer than our budget
    return ValueTask.FromResult(delay is null || delay <= maxRetryDelay);
};

// MUST be called AFTER assigning ShouldHandle so it wraps the custom predicate:
options.Retry.DisableForUnsafeHttpMethods();
```

---

## Partial failures and fallbacks

For non-critical downstream enrichments (recommendations, analytics, non-authoritative metadata), degrade gracefully instead of failing the primary request:

- Use a short timeout and fallback value (`FallbackStrategyOptions<T>`) or serve stale cached data when the downstream circuit is open.
- Never silently swallow failures on authoritative writes, payments, or security checks—fail closed and propagate the error.

---

## Resilience anti-patterns

❌ Retrying non-idempotent `POST`/`DELETE` mutations without idempotency keys or deduplication.

❌ Retrying without jitter or without a bounded `MaxRetryAttempts`.

❌ Wrapping `DbContext.SaveChangesAsync()` in a generic retry loop that reuses the same `DbContext` instance.

❌ Setting `AttemptTimeout` >= `TotalRequestTimeout`, leaving zero budget for retries.

❌ Blindly awaiting unbounded `Retry-After` delays (e.g., 1 hour) inside synchronous request handlers.

❌ Enabling `UseRequestTimeouts()` without propagating `CancellationToken` through handlers, EF Core queries, and outbound I/O.
