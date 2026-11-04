namespace TenantDocs;

public sealed partial class MaintenancePass(ILogger<MaintenancePass> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Scoped maintenance pass completed.")]
    private static partial void LogPass(ILogger logger);
    public Task RunAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LogPass(logger);
        return Task.CompletedTask;
    }
}

public sealed class MaintenanceWorker(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MaintenancePass>().RunAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
