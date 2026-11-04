using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

var results = new List<object>();
var failures = 0;
async Task Check(string name, Func<Task> test)
{
    try { await test(); results.Add(new { name, status = "Observed", error = "" }); }
    catch (Exception ex) { failures++; results.Add(new { name, status = "HarnessFailure", error = ex.ToString() }); }
}
void Require(bool value, string reason)
{
    if (!value) throw new InvalidOperationException(reason);
}

await Check("TenantHeader_AuthenticatedWithoutClaim_AcceptsArbitraryTenant", async () =>
{
    var requested = Guid.NewGuid();
    var http = new DefaultHttpContext();
    http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user")], "audit"));
    http.Request.Headers["X-Tenant-Id"] = requested.ToString();
    var nextCalled = false;
    var middleware = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
    var tenant = new TenantContext();
    await middleware.InvokeAsync(http, tenant);
    Require(nextCalled && tenant.TenantId == requested, "Header fallback behavior differs from audited example");
});

await Check("TenantConnection_MissingMapping_FallsBackToSharedDatabase", () =>
{
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = "shared-database" }).Build();
    var factory = new TenantConnectionFactory(new TenantContext { TenantId = Guid.NewGuid() }, config);
    Require(factory.GetConnectionString() == "shared-database", "Expected documented fallback");
    return Task.CompletedTask;
});

await Check("KeyedLock_CompletedDistinctKeys_RemainRetained", async () =>
{
    var manager = new KeyedLockManager();
    for (var i = 0; i < 1000; i++) await manager.ExecuteAsync(i.ToString(), () => Task.CompletedTask, default);
    var map = (System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>)
        typeof(KeyedLockManager).GetField("_locks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
    Require(map.Count == 1000, "Expected retained locks");
});

await Check("ExceptionHandler_Production_BypassesProblemDetailsCustomization", async () =>
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
    builder.Logging.ClearProviders();
    builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["audit"] = "expected");
    await using var app = builder.Build();
    var http = new DefaultHttpContext { RequestServices = app.Services };
    http.Response.Body = new MemoryStream();
    var handler = new GlobalExceptionHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalExceptionHandler>.Instance);
    await handler.TryHandleAsync(http, new InvalidOperationException("sensitive"), default);
    http.Response.Body.Position = 0;
    var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
    Require(http.Response.ContentType!.StartsWith("application/json"), "Content type behavior changed");
    using var json = JsonDocument.Parse(body);
    Require(!json.RootElement.TryGetProperty("audit", out _) && !body.Contains("sensitive"), "Customization bypass or detail suppression differs");
});

await Check("Resilience_StandardHandler_RetriesUnsafePostByDefault", async () =>
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Services.AddHttpClient("audit").ConfigurePrimaryHttpMessageHandler(() => new CountingHandler())
        .AddStandardResilienceHandler(o => { o.Retry.Delay = TimeSpan.Zero; o.Retry.UseJitter = false; });
    using var host = builder.Build();
    using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("audit");
    CountingHandler.Calls = 0;
    using var response = await client.PostAsync("https://audit.invalid/", new StringContent("{}"));
    Require(CountingHandler.Calls == 4, $"Expected initial request + 3 retries; got {CountingHandler.Calls}");
});

var connectionString = Environment.GetEnvironmentVariable("DOTNET_AUDIT_SQL");
if (string.IsNullOrWhiteSpace(connectionString))
{
    failures++;
    results.Add(new { name = "SqlProofs", status = "Unverified", error = "DOTNET_AUDIT_SQL is required; must point only to a disposable audit database." });
}
else
{
    if (new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).InitialCatalog != "Audit")
        throw new InvalidOperationException("Use only a disposable database named Audit.");
    var tenantA = new TenantContext { TenantId = Guid.NewGuid() };
    var tenantB = new TenantContext { TenantId = Guid.NewGuid() };
    var aId = Guid.NewGuid(); var bId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
    // Only CREATE/INSERT/UPDATE/DELETE in a user-supplied disposable audit DB; never drop databases.
    await using (var seed = new AppDbContext(options, tenantA))
    {
        await seed.Database.EnsureCreatedAsync();
        seed.Customers.AddRange(new Customer { Id = aId, TenantId = tenantA.TenantId, Name = "A" },
                                new Customer { Id = bId, TenantId = tenantB.TenantId, Name = "B" });
        await seed.SaveChangesAsync();
    }
    await Check("TenantFilter_SecondContext_ReusesFirstTenant", async () =>
    {
        await using var a = new AppDbContext(options, tenantA);
        await using var b = new AppDbContext(options, tenantB);
        var aRows = await a.Customers.Where(c => c.Id == aId || c.Id == bId).ToListAsync();
        var bRows = await b.Customers.Where(c => c.Id == aId || c.Id == bId).ToListAsync();
        Require(aRows.Count == 1 && aRows[0].Id == aId, "A filter did not return A row");
        Require(bRows.Count == 1 && bRows[0].Id == aId, "Expected B context to reproduce A capture");
        Require(ReferenceEquals(a.Model, b.Model), "Contexts must share model for this proof");
    });
    await Check("TenantFilter_ContextProperty_ControlIsIsolated", async () =>
    {
        var safeOptions = new DbContextOptionsBuilder<ControlDb>().UseSqlServer(connectionString).Options;
        await using var a = new ControlDb(safeOptions, tenantA.TenantId);
        await using var b = new ControlDb(safeOptions, tenantB.TenantId);
        Require(await a.Customers.AnyAsync(c => c.Id == aId) && !await a.Customers.AnyAsync(c => c.Id == bId), "A isolation control failed");
        Require(await b.Customers.AnyAsync(c => c.Id == bId) && !await b.Customers.AnyAsync(c => c.Id == aId), "B isolation control failed");
    });
    await Check("TenantInterceptor_AttachedForeignEntity_UpdatesOtherTenant", async () =>
    {
        var interceptedOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString)
            .AddInterceptors(new TenantSaveChangesInterceptor(tenantA)).Options;
        await using (var db = new AppDbContext(interceptedOptions, tenantA))
        {
            var foreign = new Customer { Id = bId, TenantId = tenantB.TenantId, Name = "altered-by-A" };
            db.Attach(foreign);
            db.Entry(foreign).Property(c => c.Name).IsModified = true;
            await db.SaveChangesAsync();
        }
        await using var verify = new AppDbContext(options, tenantB);
        Require((await verify.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == bId)).Name == "altered-by-A", "Expected attached write bypass");
    });
    await Check("TenantInterceptor_UnresolvedContext_AllowsInsert", async () =>
    {
        var unresolved = new TenantContext();
        var interceptedOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString)
            .AddInterceptors(new TenantSaveChangesInterceptor(unresolved)).Options;
        var id = Guid.NewGuid();
        await using (var db = new AppDbContext(interceptedOptions, unresolved))
        {
            db.Customers.Add(new Customer { Id = id, TenantId = tenantB.TenantId, Name = "unresolved" });
            await db.SaveChangesAsync();
        }
        await using var verify = new AppDbContext(options, tenantB);
        Require(await verify.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == id), "Expected unresolved insert");
    });
}
Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;

sealed class ControlDb(DbContextOptions<ControlDb> options, Guid tenantId) : DbContext(options)
{
    public Guid TenantId { get; } = tenantId;
    public DbSet<Customer> Customers => Set<Customer>();
    protected override void OnModelCreating(ModelBuilder b) => b.Entity<Customer>().HasQueryFilter(c => c.TenantId == TenantId);
}
sealed class CountingHandler : HttpMessageHandler
{
    public static int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }
}
