# Testing Engineering

Use this reference when designing, writing, repairing, or scaffolding tests in modern .NET.

## Official references

- Microsoft Testing Platform (MTP): https://learn.microsoft.com/dotnet/core/testing/unit-testing-platform-overview
- MSTest.Sdk: https://learn.microsoft.com/dotnet/core/testing/unit-testing-mstest-sdk
- xUnit v3: https://xunit.net/docs/getting-started/v3/cmdline
- Testcontainers for .NET: https://dotnet.testcontainers.org/
- WebApplicationFactory: https://learn.microsoft.com/aspnet/core/test/integration-tests
- Respawn: https://github.com/jbogard/Respawn
- FakeTimeProvider: https://learn.microsoft.com/dotnet/api/microsoft.extensions.time.testing.faketimeprovider

---

## Core testing principles

1. **Test behavior, not implementation.** Call code the way its users or HTTP consumers do. Assert externally observable outputs, state changes, and responses. Never write tests that break when internal private methods or implementation details are refactored.
2. **Match test type to risk boundary:**
   - **Unit tests:** For pure domain logic, complex algorithms, validators, and deterministic state transitions. Fast, zero I/O, no network or database.
   - **Integration tests (`WebApplicationFactory`):** For HTTP routing, middleware, model binding, authorization, status codes, and serialization.
   - **Persistence tests (`Testcontainers`):** For real database queries, migrations, transactions, and concurrency behavior.
3. **No in-memory databases for relational testing.** The EF Core In-Memory provider (`UseInMemoryDatabase`) does not enforce foreign keys, unique constraints, transactions, column types, or database-specific SQL functions. It gives false confidence. Always use Testcontainers for relational database tests.
4. **Deterministic time.** Never call `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly in testable code. Inject `TimeProvider` and control time in tests using `FakeTimeProvider`.

---

## Framework standards

### Primary default: current MSTest.Sdk (resolve version)

For greenfield projects, prefer **current MSTest.Sdk**. It runs natively on the **Microsoft Testing Platform (MTP)**, compiles as a test executable, and eliminates traditional runner boilerplate.

SDK version should be specified in `global.json` under `msbuild-sdks`:
```json
{
  "msbuild-sdks": {
    "MSTest.Sdk": "3.8.2"
  }
}
```
Or directly on the project root element: `<Project Sdk="MSTest.Sdk/3.8.2">`.

#### Project File (`.csproj`)
```xml
<Project Sdk="MSTest.Sdk">
  <PropertyGroup>
    <!-- Use repository target framework; default to active LTS net10.0 for greenfield -->
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Resolve compatible package versions dynamically in Directory.Packages.props under CPM -->
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Respawn" />
  </ItemGroup>
</Project>
```

---

### Alternative standard: xUnit v3

For repositories with established xUnit conventions, or teams that prefer constructor-injected fixtures (`IClassFixture<T>`, `ICollectionFixture<T>`), use **xUnit v3**.

#### Project File (`.csproj`)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Use repository target framework; default to active LTS net10.0 for greenfield -->
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Resolve compatible package versions dynamically in Directory.Packages.props under CPM -->
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Respawn" />
  </ItemGroup>
</Project>
```

---

## Persistence testing with Testcontainers

When testing Entity Framework Core, Dapper, or raw SQL queries, use **Testcontainers**. It spins up an ephemeral, real Docker container for your database.

### 1. The Database Fixture (MSTest)

Manage container lifecycle using `[AssemblyInitialize]` and `[AssemblyCleanup]` (or `[ClassInitialize]`):

```csharp
using Testcontainers.PostgreSql;

[TestClass]
public static class DatabaseFixture
{
    private static readonly PostgreSqlContainer Container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("test_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public static string ConnectionString => Container.GetConnectionString();

    [AssemblyInitialize]
    public static async Task AssemblyInitialize(TestContext context)
    {
        await Container.StartAsync();
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
    {
        await Container.DisposeAsync();
    }
}
```

---

### 2. Custom `WebApplicationFactory` Integration

Override the application's database configuration with the container's dynamic connection string using `builder.UseSetting`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

public class CustomWebApplicationFactory<TProgram>(string connectionString) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override connection string via configuration rather than mutating DI service descriptors.
        // This preserves DbContext pooling, interceptors, and provider registrations.
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
    }
}
```

---

### 3. Fast Table Truncation with Respawn

Restarting containers between tests is too slow (5–10s). Re-creating database schemas between tests is also slow. Instead, use **Respawn** to truncate tables in ~5ms between test runs.

> [!IMPORTANT]
> When multiple test methods or classes share a single database container reset by Respawn, you must prevent parallel execution on that test suite (e.g. using `[DoNotParallelize]` in MSTest) to avoid race conditions.

```csharp
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
```

---

## Alternative: xUnit v3 Implementation Pattern

For xUnit v3, use `IAsyncLifetime` on class or collection fixtures:

```csharp
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
```

---

## Deterministic time testing

Avoid calling `DateTime.UtcNow` directly in business logic because it introduces non-deterministic test behavior. Inject `TimeProvider` instead so that time-dependent logic (deadlines, expirations, schedules) can be deterministically controlled in tests with `FakeTimeProvider`:

```csharp
// In production service:
public class TrialService(TimeProvider timeProvider)
{
    public bool IsExpired(DateTimeOffset startedAt) =>
        timeProvider.GetUtcNow() - startedAt > TimeSpan.FromDays(14);
}

// In test method:
[TestMethod]
public void Trial_After15Days_IsExpired()
{
    var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
    var service = new TrialService(fakeTime);
    var start = fakeTime.GetUtcNow();

    fakeTime.Advance(TimeSpan.FromDays(15));

    Assert.IsTrue(service.IsExpired(start));
}
```

---

## Anti-patterns to reject

- **Mocking DbContext / DbSet:** Mocks cannot simulate relational expressions, joins, tracking state, or SQL translation errors.
- **In-Memory EF Core Provider:** Does not enforce foreign keys or constraints. Use Testcontainers.
- **Testing Private Methods:** If private logic is complex enough to need independent testing, extract it into a focused, pure domain collaborator.
- **Testing Frameworks in Unit Tests:** Do not test whether EF Core or ASP.NET Core works; test your business rules and mapping logic.
