using AuditContracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
var builder = Host.CreateApplicationBuilder();
builder.Services.AddDbContext<AppDbContext>(o=>o.UseSqlServer("Server=127.0.0.1,52457;Database=MessagingProof;User Id=sa;Password=AuditOnly_2026_Strong_Pass;TrustServerCertificate=True"));
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

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await db.Database.EnsureCreatedAsync();
var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
try { await publish.Publish(new OrderCreatedEvent(Guid.NewGuid())); Console.WriteLine("Unexpectedly published without model setup"); return 1; }
catch (InvalidOperationException e) { Console.WriteLine(e.Message); if (!e.Message.Contains("Outbox")) throw; }
Console.WriteLine("Observed: original bus registration needs explicit outbox model entities");
return 0;
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

namespace AuditContracts { public record OrderCreatedEvent(Guid OrderId); }
public sealed class Order { public Guid Id {get;set;} public Guid CustomerId {get;set;} public bool IsProcessed {get;set;} }
public sealed class AppDbContext(DbContextOptions<AppDbContext> o):DbContext(o) { public DbSet<Order> Orders=>Set<Order>(); }
