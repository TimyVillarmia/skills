using Xunit;
using Testcontainers.PostgreSql;
using System.Net;
using System.Net.Http.Json;
public class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

public class OrdersApiTests(CustomApiFactory factory) : IClassFixture<CustomApiFactory>
{
    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsCreated()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/orders", new CreateOrderRequest("ItemA", 5));
        
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
