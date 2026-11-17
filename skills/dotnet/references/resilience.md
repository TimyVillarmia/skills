# Resilience & Fault Tolerance

Use this reference when configuring outbound HTTP resilience, non-HTTP `ResiliencePipeline` strategies, retry/timeout budgets, circuit breakers, or upstream `429` handling.

## Contents

- [Outbound HTTP resilience](#outbound-http-resilience)
- [Non-HTTP ResiliencePipeline](#non-http-resiliencepipeline)
- [Retry safety and EF Core trap](#retry-safety-and-ef-core-trap)
- [Timeout budgets and circuit breakers](#timeout-budgets-and-circuit-breakers)
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
    client.Timeout = TimeSpan.FromSeconds(15);
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
> `Microsoft.Extensions.Http.Resilience` is published under Microsoft's standard package license. Polly 8.x is published under Apache-2.0 / BSD-3; maintainer licensing announcements apply only to future post-2026 majors, and transitive usage via Microsoft packages remains covered by Microsoft's license.

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

$$\text{TotalRequestTimeout} > \text{AttemptTimeout} \times (\text{MaxRetryAttempts} + 1) + \text{TotalBackoff}$$

- Set **Attempt Timeout** tight enough to fail hung sockets quickly.
- Set **Total Request Timeout** to the caller's SLA budget.
- Configure **Circuit Breaker** `MinimumThroughput` and `SamplingDuration` high enough to avoid tripping on a handful of isolated errors during low traffic.

---

## Honoring upstream `429` and `Retry-After`

When calling external APIs that return `429 Too Many Requests` or `503 Service Unavailable` with a `Retry-After` header, respect the server's backoff hint—but **clamp** the delay to a bounded ceiling so an upstream returning `Retry-After: 3600` does not pin request threads or exhaust connection pools:

```csharp
options.Retry.DelayGenerator = args =>
{
    var retryAfter = args.Outcome.Result?.Headers.RetryAfter;
    var delay = retryAfter?.Delta
        ?? (retryAfter?.Date is DateTimeOffset date ? date - TimeProvider.System.GetUtcNow() : (TimeSpan?)null);

    if (delay is > TimeSpan.Zero)
    {
        var maxDelay = TimeSpan.FromSeconds(15);
        return ValueTask.FromResult<TimeSpan?>(delay.Value > maxDelay ? maxDelay : delay.Value);
    }

    return ValueTask.FromResult<TimeSpan?>(null); // Fall back to default exponential backoff + jitter
};
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
