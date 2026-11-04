using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
var context = new DefaultHttpContext();
context.Response.Body = new MemoryStream();
var entry = new HealthReportEntry(HealthStatus.Unhealthy,"health failure",TimeSpan.Zero,new Exception("SECRET_TEST_VALUE"),null);
await HealthWriter.Write(context,new HealthReport(new Dictionary<string,HealthReportEntry>{{"db",entry}},TimeSpan.Zero));
context.Response.Body.Position=0;
var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
if(!json.Contains("SECRET_TEST_VALUE"))throw new Exception("probe reproduction failed");
Console.WriteLine("Observability: health response exposes exception marker: " + json);
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

public static class HealthWriter {
 public static Task Write(HttpContext context, HealthReport report) => WriteJsonResponse(context,report);
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

}
