using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.MsSql;

namespace TenantDocs.Tests;

[TestClass]
public sealed class DocumentsApiTests
{
    [TestMethod]
    public async Task AlternatingTenantsCannotReadOrModifyEachOthersDocuments()
    {
        await using var database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using var factory = new ApiFactory(database.GetConnectionString());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var documentA = Guid.NewGuid();
        var documentB = Guid.NewGuid();
        foreach (var (tenant, document, title) in new[] { (tenantA, documentA, "A"), (tenantB, documentB, "B") })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = PrincipalContext(tenant.ToString());
            var db = scope.ServiceProvider.GetRequiredService<DocumentsDb>();
            await db.Database.EnsureCreatedAsync();
            db.Documents.Add(new Document { Id = document, Title = title });
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Remove("Cookie");
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync($"/documents/{documentA}")).StatusCode);
        SetCookie(client, factory, null);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantA.ToString());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync($"/documents/{documentA}")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");

        for (var iteration = 0; iteration < 3; iteration++)
        {
            foreach (var (tenant, own, other, label) in new[] { (tenantA, documentA, documentB, "A"), (tenantB, documentB, documentA, "B") })
            {
                SetCookie(client, factory, tenant.ToString());
                Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/documents/{other}")).StatusCode);
                Assert.AreEqual(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/documents/{other}", new { title = "hacked", tenantId = tenant })).StatusCode);
                var ownResponse = await client.GetFromJsonAsync<DocumentResponse>($"/documents/{own}");
                Assert.IsNotNull(ownResponse);
                Assert.AreEqual(iteration == 0 ? label : $"{label}-{iteration - 1}", ownResponse.Title);
                var update = await client.PutAsJsonAsync($"/documents/{own}", new { title = $"{label}-{iteration}" });
                Assert.AreEqual(HttpStatusCode.NoContent, update.StatusCode);
                Assert.AreEqual(0, (await update.Content.ReadAsByteArrayAsync()).Length);
            }
        }
        SetCookie(client, factory, tenantA.ToString());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/documents/{documentA}", new { title = " " })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/documents/{Guid.NewGuid()}")).StatusCode);
        foreach (var (tenant, document, expected) in new[] { (tenantA, documentA, "A-2"), (tenantB, documentB, "B-2") })
        {
            SetCookie(client, factory, tenant.ToString());
            Assert.AreEqual(expected, (await client.GetFromJsonAsync<DocumentResponse>($"/documents/{document}"))!.Title);
        }
    }

    [TestMethod]
    public async Task WorkerDisposesScopedDependencyAndStopsPromptly()
    {
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ScopeProbe(disposed));
        services.AddScoped<MaintenancePass>(provider =>
        {
            _ = provider.GetRequiredService<ScopeProbe>();
            return new MaintenancePass(provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MaintenancePass>>());
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var worker = new MaintenanceWorker(provider.GetRequiredService<IServiceScopeFactory>());
        await worker.StartAsync(CancellationToken.None);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StopAsync(stop.Token);
        Assert.IsFalse(stop.IsCancellationRequested);
        Assert.IsNotNull(worker.ExecuteTask);
        Assert.IsTrue(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    private sealed class ScopeProbe(TaskCompletionSource disposed) : IDisposable
    {
        public void Dispose() => disposed.TrySetResult();
    }

    private static DefaultHttpContext PrincipalContext(string? tenant)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "integration-user") };
        if (tenant is not null) claims.Add(new("tenant_id", tenant));
        return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)) };
    }

    private static void SetCookie(HttpClient client, ApiFactory factory, string? tenant)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(PrincipalContext(tenant).User, CookieAuthenticationDefaults.AuthenticationScheme);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"TenantDocs.Auth={options.TicketDataFormat.Protect(ticket)}");
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("Store:ConnectionString", connectionString);
    }
}
