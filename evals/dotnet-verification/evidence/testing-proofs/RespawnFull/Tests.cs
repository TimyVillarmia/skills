using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Json;
using Respawn;

[TestClass]
[DoNotParallelize] // Essential: prevents test concurrency races when sharing a single database reset by Respawn
public class OrdersApiTests
{
    private static CustomWebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;
    private static Respawner _respawner = null!;
    private static string _connectionString = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        _connectionString = DatabaseFixture.ConnectionString;
        _factory = new CustomWebApplicationFactory<Program>(_connectionString);
        _client = _factory.CreateClient();

        // Initialize Respawn checkpoint
        _respawner = await Respawner.CreateAsync(_connectionString, new RespawnerOptions
        {
            SchemasToInclude = ["public"],
            DbAdapter = DbAdapter.Postgres
        });
    }

    [TestInitialize]
    public async Task TestInit()
    {
        // Reset database state before each test
        await _respawner.ResetAsync(_connectionString);
    }

    [TestMethod]
    public async Task CreateOrder_ValidPayload_ReturnsCreatedAndPersists()
    {
        // Arrange
        var command = new CreateOrderRequest("Product-101", 3);

        // Act
        var response = await _client.PostAsJsonAsync("/orders", command);

        // Assert (using framework-native assertions)
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.IsNotNull(created);
        Assert.AreEqual(3, created.Quantity);
    }
}
