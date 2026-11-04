using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/orders/{id:guid}", async Task<IResult> (
    Guid id,
    OrderDbContext db,
    IAuthorizationService authService,
    ClaimsPrincipal user,
    CancellationToken ct) =>
{
    var order = await db.Orders.FindAsync([id], ct);
    if (order is null)
    {
        return TypedResults.NotFound();
    }

    var authResult = await authService.AuthorizeAsync(user, order, Operations.Read);
    if (!authResult.Succeeded)
    {
        return TypedResults.Forbid();
    }

    return TypedResults.Ok(OrderResponse.From(order));
});

app.Run();
public sealed class OrderDbContext { public OrderSet Orders {get;} = new(); }
public sealed class OrderSet { public ValueTask<Order?> FindAsync(object[] ids, CancellationToken ct) => ValueTask.FromResult<Order?>(null); }
public sealed record Order(string CustomerId);
public sealed record OrderResponse { public static OrderResponse From(Order order) => new(); }
public static class Operations { public static readonly Microsoft.AspNetCore.Authorization.Infrastructure.OperationAuthorizationRequirement Read = new(){Name="Read"}; }
