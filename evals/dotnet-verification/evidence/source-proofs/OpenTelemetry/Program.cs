using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
var builder = WebApplication.CreateBuilder(args);
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

Console.WriteLine("OTel registration constructed");