using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSingleton<IOrderService,OrderService>();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddAuthentication("proof").AddScheme<AuthenticationSchemeOptions,ProofAuth>("proof", _ => {});
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapOrderEndpoints();
var json=JsonSerializer.Serialize(new OrderResponse(ProofIds.Existing,"check"),AppJsonSerializerContext.Default.OrderResponse);
if(!json.Contains("\"name\":\"check\""))throw new Exception("JSON context failed");
Console.WriteLine("JSON context camelCase check passed: "+json);
app.Run();
public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            .RequireAuthorization();

        group.MapGet("/", GetOrders);
        group.MapGet("/{id:guid}", GetOrderById);
        group.MapPost("/", CreateOrder);

        return group;
    }

    private static Ok<OrderDto[]> GetOrders() => TypedResults.Ok(new[] { new OrderDto(ProofIds.Existing,"example") });
    private static Created<OrderResponse> CreateOrder(CreateOrderRequest request) => TypedResults.Created("/api/orders/" + ProofIds.Existing,new OrderResponse(ProofIds.Existing, request.Name));

    private static async Task<Results<Ok<OrderDto>, NotFound>> GetOrderById(
        Guid id,
        IOrderService service,
        CancellationToken ct)
    {
        var order = await service.GetByIdAsync(id, ct);
        return order is not null ? TypedResults.Ok(order) : TypedResults.NotFound();
    }
}
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(OrderDto[]))]
[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(OrderResponse))]
public sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}

public sealed record CreateOrderRequest(string Name);
public sealed record OrderResponse(Guid Id,string Name);
public sealed record OrderDto(Guid Id,string Name);
public static class ProofIds { public static readonly Guid Existing=Guid.Parse("11111111-1111-1111-1111-111111111111"); }
public interface IOrderService { Task<OrderDto?> GetByIdAsync(Guid id,CancellationToken ct); }
public sealed class OrderService : IOrderService { public Task<OrderDto?> GetByIdAsync(Guid id,CancellationToken ct) => Task.FromResult(id==ProofIds.Existing ? new OrderDto(id,"example") : null); }
// Test-only authenticated identity for exercising RequireAuthorization without any live identity provider.
public sealed class ProofAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
 protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization=="Proof allowed" ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"proof")],"proof")),Scheme.Name)) : AuthenticateResult.NoResult());
}
