using System.Threading.Channels;
var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(15);
});

var queue = new WorkQueue<int>();
for(var i=0;i<1000;i++) await queue.EnqueueAsync(i);
using var cancelled = new CancellationTokenSource();
var blocked=queue.EnqueueAsync(1000,cancelled.Token);
if(blocked.IsCompleted) throw new Exception("queue failed backpressure");
cancelled.Cancel();
try {await blocked;throw new Exception("enqueue ignored cancellation");} catch(OperationCanceledException){}
Console.WriteLine("Hosted: bounded queue backpressure and cancellation passed");
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

public sealed class ProcessPendingOrdersHandler { public Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask; }
public sealed record WorkItem(int Id);
public interface IItemProcessor { Task ProcessAsync(WorkItem item,CancellationToken ct); }
