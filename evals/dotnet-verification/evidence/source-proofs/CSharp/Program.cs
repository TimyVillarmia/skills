using System.Text.Json;
using System.Text.Json.Serialization;
int[] values = [1, 2, 3];
List<string> names = ["Ada", "Grace"];

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var order = new Order(OrderStatus.Pending);
if(!order.IsOpen) throw new Exception("extension failure");
var named = new Named(); named.Name = "  Tim  ";
if(named.Name != "Tim") throw new Exception("field failure");
var json = JsonSerializer.Serialize(new OrderResponse(1), AppJsonSerializerContext.Default.OrderResponse);
if(json != "{\"id\":1}") throw new Exception("json failure: " + json);
Console.WriteLine("CSharp: field, extension, collection and JSON checks passed");
public class Named {
public string Name
{
    get;
    set => field = value.Trim();
}

}
public sealed class OrderService(IOrderRepository orders, TimeProvider clock)
{
    public async Task<Order?> GetAsync(Guid id, CancellationToken ct)
        => await orders.GetAsync(id, ct);
}
public static class OrderExtensions
{
    extension(Order order)
    {
        public bool IsOpen => order.Status is OrderStatus.Pending or OrderStatus.Processing;
    }
}
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(OrderResponse))]
public sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}

public enum OrderStatus { Pending, Processing, Complete }
public sealed record Order(OrderStatus Status);
public interface IOrderRepository { Task<Order?> GetAsync(Guid id, CancellationToken ct); }
public sealed record CreateOrderRequest(int Id);
public sealed record OrderResponse(int Id);
