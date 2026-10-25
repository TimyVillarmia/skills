# Observability & Diagnostics

Use this reference when configuring telemetry, metrics, logging, health probes, or sensitive data protection.

## Core Observability Decisions

| When designing | Do (Recommended) | Never (Anti-pattern) | Verify (How to validate) |
|---|---|---|---|
| **Distributed Tracing** | Instrument domain boundaries with `ActivitySource("MyCompany.Orders")` and export via OpenTelemetry OTLP | Rely on ambient HTTP headers manually or create un-parented activities | Trace displays in Jaeger/Aspire/OTel collector with connected span hierarchy |
| **Operational Metrics** | Track throughput, rates, and durations with `Meter` (`Counter<T>`, `Histogram<T>`, `UpDownCounter<T>`) | Parse unstructured text logs to extract operational numbers | Metrics visible on `/metrics` endpoint or exported OTLP collector |
| **Structured Logging** | Use compile-time `[LoggerMessage]` source generator with named placeholders | Use string interpolation (`$"User {id}"`) or un-templated strings in log calls | Log events emit structured JSON attributes without string allocations |
| **Health Checks** | Distinguish liveness (`/healthz/live`) from readiness (`/healthz/ready`) using built-in JSON writer | Put deep database queries inside liveness probes (causes restart cascade) | Probes return 200 OK when healthy and 503 Service Unavailable when failing |
| **Sensitive Data** | Redact PII, tokens, and credentials using `Microsoft.Extensions.Compliance.Redaction` | Log raw request/response payloads containing passwords or auth headers | Log outputs contain sanitized redaction tokens (e.g. `[REDACTED]`) |

---

## OpenTelemetry Tracing & Metrics

Configure tracing and metrics through framework-provided builder extensions (`Microsoft.Extensions.Hosting` / OpenTelemetry):

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("OrderService")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("OrderService")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
```

### Emitting Domain Activities and Metrics

```csharp
public static class OrderTelemetry
{
    private static readonly ActivitySource Source = new("OrderService");
    private static readonly Meter Meter = new("OrderService");
    private static readonly Counter<long> OrdersCreatedCounter = Meter.CreateCounter<long>("orders.created");

    public static Activity? StartOrderCreation(string customerId)
    {
        var activity = Source.StartActivity("CreateOrder", ActivityKind.Internal);
        activity?.SetTag("customer.id", customerId);
        OrdersCreatedCounter.Add(1, new KeyValuePair<string, object?>("customer.id", customerId));
        return activity;
    }
}
```

---

## High-Performance Logging with `[LoggerMessage]`

Avoid runtime string formatting allocations on frequently called execution paths:

```csharp
public static partial class LogMessages
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Order {OrderId} created for customer {CustomerId} with total {TotalAmount:C}")]
    public static partial void OrderCreated(
        ILogger logger, Guid orderId, string customerId, decimal totalAmount);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Order {OrderId} failed payment processing: {ErrorCode}")]
    public static partial void PaymentFailed(
        ILogger logger, Guid orderId, string errorCode, Exception? ex);
}
```

---

## Health Checks: Liveness vs Readiness

Configure separate probes for Kubernetes and container runtimes. Never query external dependencies in liveness probes; doing so causes cascading pod restarts and cluster instability when a downstream dependency experiences transient latency. Query external dependencies in readiness probes instead.

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connectionString, tags: ["ready"]);

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteJsonResponse
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteJsonResponse
});
```

### Built-in JSON Response Writer
Always use native `System.Text.Json` rather than unneeded third-party packages:

```csharp
private static Task WriteJsonResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var response = new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        entries = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            durationMs = e.Value.Duration.TotalMilliseconds,
            description = e.Value.Description,
            error = e.Value.Exception?.Message
        })
    };
    return JsonSerializer.SerializeAsync(context.Response.Body, response);
}
```

---

## Data Redaction & Compliance

Use `Microsoft.Extensions.Compliance.Redaction` to ensure credentials and personal identifiers never reach log aggregators:

```csharp
builder.Logging.EnableRedaction();
builder.Services.AddRedaction(options =>
{
    options.SetRedactor<ErasingRedactor>(new DataClassificationSet(DataTaxonomy.SensitiveData));
});

public static class DataTaxonomy
{
    public static DataClassification SensitiveData => new("Security", "Sensitive");
}
```
