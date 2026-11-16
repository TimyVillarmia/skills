# Testing with FastEndpoints.Testing

FastEndpoints includes the official **`FastEndpoints.Testing`** package, which provides a strongly-typed, high-performance testing harness built on top of `WebApplicationFactory`.

---

## 1. Project Setup (`.csproj`)

Add `FastEndpoints.Testing` to your test project alongside your test framework (MSTest or xUnit):

```xml
<ItemGroup>
  <PackageReference Include="FastEndpoints.Testing" />
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  <PackageReference Include="MSTest.Sdk" /> <!-- Or xUnit.v3 -->
</ItemGroup>
```

---

## 2. Defining the `AppFixture`

Create a fixture inheriting from `AppFixture<TProgram>`. This encapsulates the web application lifecycle, database containers, and mock overrides:

```csharp
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Testcontainers.PostgreSql;

namespace MyApp.Tests.Fixtures;

public class App : AppFixture<Program>
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    protected override async Task PreSetupAsync()
    {
        // Start Docker container before WebApplicationFactory starts
        await _dbContainer.StartAsync();
    }

    protected override void ConfigureApp(IWebHostBuilder builder)
    {
        // Override connection string in memory to satisfy ValidateOnStart()
        builder.UseSetting("ConnectionStrings:DefaultConnection", _dbContainer.GetConnectionString());
    }

    protected override async Task TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
    }
}
```

---

## 3. Writing Strongly-Typed Tests

Inherit tests from `TestClass<TFixture>`. Use the typed `Client` extensions to invoke endpoints by type name without hardcoding URL strings:

### Testing an Endpoint with Request and Response
```csharp
using System.Net;
using FastEndpoints.Testing;
using MyApp.Features.Orders.CreateOrder;
using MyApp.Tests.Fixtures;

namespace MyApp.Tests.Orders;

[TestClass]
public class CreateOrderTests(App app) : TestClass<App>(app)
{
    [TestMethod]
    public async Task CreateOrder_WithValidPayload_ReturnsCreated()
    {
        // 1. Arrange request DTO
        var request = new CreateOrderRequest
        {
            CustomerId = "cust-100",
            Amount = 149.99m
        };

        // 2. Act: Call endpoint using strongly-typed generic method
        // Automatically resolves route template, HTTP verb, and JSON serialization
        var (rsp, res) = await App.Client.POSTAsync<CreateOrderEndpoint, CreateOrderRequest, CreateOrderResponse>(request);

        // 3. Assert
        Assert.AreEqual(HttpStatusCode.Created, rsp.StatusCode);
        Assert.IsNotNull(res);
        Assert.IsFalse(string.IsNullOrWhiteSpace(res.OrderId));
    }

    [TestMethod]
    public async Task CreateOrder_WithInvalidPayload_ReturnsProblemDetails()
    {
        var request = new CreateOrderRequest
        {
            CustomerId = "", // Violates NotEmpty rule
            Amount = -10m    // Violates positive amount rule
        };

        var (rsp, res) = await App.Client.POSTAsync<CreateOrderEndpoint, CreateOrderRequest, ProblemDetails>(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, rsp.StatusCode);
        Assert.IsNotNull(res);
        Assert.IsTrue(res.Errors.ContainsKey("CustomerId") || res.Errors.ContainsKey("customerId"));
    }
}
```

---

## 4. Testing Endpoints Without Request Bodies

For `GET` or parameterless endpoints:

```csharp
[TestMethod]
public async Task GetStatus_ReturnsHealthy()
{
    var (rsp, res) = await App.Client.GETAsync<GetSystemStatusEndpoint, StatusResponse>();

    Assert.AreEqual(HttpStatusCode.OK, rsp.StatusCode);
    Assert.AreEqual("Healthy", res.Status);
}
```

---

## 5. Authenticated Testing Clients

Create pre-configured authenticated clients with claims and permissions:

```csharp
[TestMethod]
public async Task DeleteUser_WithAdminPermission_Succeeds()
{
    // Generate test client with specific claims/permissions
    var adminClient = App.CreateClient(c =>
    {
        c.AddHeader("Authorization", "Bearer " + GenerateTestJwtToken("AdminUserId", ["Orders:Delete"]));
    });

    var (rsp, _) = await adminClient.DELETEAsync<DeleteUserEndpoint, EmptyResponse>();

    Assert.AreEqual(HttpStatusCode.NoContent, rsp.StatusCode);
}
```

---

## 6. Testing Principles with FastEndpoints

1. **Invoke by endpoint type (`POSTAsync<TEndpoint, ...>`)**: Do not hardcode URL paths like `client.PostAsync("/api/v1/orders", ...)`. Using endpoint types guarantees tests fail at compile-time if routes or DTO contracts change.
2. **Preserve startup validation (`ValidateOnStart`)**: Pass container connection strings or test settings via `builder.UseSetting()` in `ConfigureApp()`. Do not bypass validation by registering unvalidated mock objects in DI.
3. **Assert Problem Details for validation failures**: FastEndpoints returns RFC 7807 problem details by default. Deserialize failure responses to `ProblemDetails` to assert specific property errors.
