window.SKILLS_SITE_DATA = {
  installCommands: {
    agy: {
      code: "agy plugin install https://github.com/TimyVillarmia/skills",
      note: "Installs all three skills (`dotnet`, `fastendpoints`, `dotnet-scaffold`) as a single Antigravity plugin."
    },
    npx: {
      code: "npx skills add TimyVillarmia/skills\n\n# Or add a single skill:\nnpx skills add TimyVillarmia/skills/skills/dotnet\nnpx skills add TimyVillarmia/skills/skills/fastendpoints\nnpx skills add TimyVillarmia/skills/skills/dotnet-scaffold",
      note: "Works with Antigravity, Claude Code, Cursor, GitHub Copilot, and any Agent Skills compatible tool."
    },
    git: {
      code: "git clone git@github.com:TimyVillarmia/skills.git ~/.agents/skills/personal",
      note: "Clone directly into `.agents/skills/` (project) or `~/.agents/skills/` (global)."
    }
  },

  scenarios: [
    {
      id: "packages",
      label: "Packages & Slice Setup",
      prompt: "Build a .NET 10 / C# 14 Order Management backend slice (Orders). Provide NuGet packages (Directory.Packages.props or .csproj) for CQRS/mediator, object mapping, and test assertions, plus a CreateOrder handler/slice that persists an Order and maps it to OrderResponse.",
      title: "Direct handler + static DTO mapping vs. MediatR / AutoMapper / FluentAssertions",
      summary: "Baseline pinned dead-end pre-commercial majors of MediatR, AutoMapper, and FluentAssertions plus legacy VSTest packages. The skill keeps zero commercial dependencies, injects CreateOrderHandler directly, and maps via OrderResponse.From / Project.",
      baseline: {
        file: "Directory.Packages.props / CreateOrder.cs",
        lang: "mixed",
        warnLines: [2, 3, 5, 6, 13, 21],
        code: `<!-- Directory.Packages.props -->
<PackageVersion Include="MediatR" Version="12.4.1" />
<PackageVersion Include="AutoMapper" Version="13.0.1" />
<PackageVersion Include="xunit" Version="2.9.3" />
<PackageVersion Include="xunit.runner.visualstudio" Version="3.0.0" />
<PackageVersion Include="FluentAssertions" Version="7.0.0" />

// CreateOrder.cs
public sealed class OrderMappingProfile : Profile
{
    public OrderMappingProfile()
    {
        CreateMap<Order, OrderResponse>()
            .ForCtorParam(nameof(OrderResponse.Status),
                opt => opt.MapFrom(src => src.Status.ToString()));
    }
}

public sealed class CreateOrderHandler(
    AppDbContext dbContext, IMapper mapper, TimeProvider timeProvider)
    : IRequestHandler<CreateOrderCommand, OrderResponse>`,
        callouts: [
          "Pins dead-end pre-commercial majors (MediatR 12, AutoMapper 13, FluentAssertions 7) where any routine upgrade hits commercial licenses (>= 13/15/8).",
          "Reflection-based AutoMapper Profile breaks Native AOT and adds startup scanning.",
          "Wires legacy VSTest runner packages instead of Microsoft.Testing.Platform."
        ]
      },
      skill: {
        file: "global.json / CreateOrder.cs",
        lang: "csharp",
        passLines: [1, 2, 8, 12, 17],
        code: `// global.json: "test": { "runner": "Microsoft.Testing.Platform" }, "MSTest.Sdk": "4.0.0"
// Zero MediatR, AutoMapper, or FluentAssertions packages

public sealed record OrderResponse(
    Guid Id, Guid TenantId, string ProductSku, int Quantity,
    decimal TotalAmount, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{
    public static OrderResponse From(Order order) => new(
        order.Id, order.TenantId, order.ProductSku, order.Quantity,
        order.TotalAmount, order.CreatedAtUtc, order.ExpiresAtUtc);

    public static Expression<Func<Order, OrderResponse>> Project => o => new OrderResponse(
        o.Id, o.TenantId, o.ProductSku, o.Quantity,
        o.TotalAmount, o.CreatedAtUtc, o.ExpiresAtUtc);
}

public sealed class CreateOrderHandler(
    AppDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
{
    public async Task<OrderResponse> HandleAsync(CreateOrderRequest req, CancellationToken ct)`,
        callouts: [
          "Zero commercial-watchlist NuGet packages; native MSTest.Sdk assertions on MTP.",
          "Direct CreateOrderHandler injection without MediatR ceremony.",
          "Compile-time OrderResponse.From and EF Core OrderResponse.Project expression."
        ]
      }
    },
    {
      id: "openapi",
      label: "Minimal API & OpenAPI",
      prompt: "Wire Program.cs with OpenAPI + Scalar API docs, error handling, and POST /api/orders + GET /api/orders/{id} Minimal API endpoints.",
      title: "Scalar ProxyUrl = null, IExceptionHandler, and AddValidation()",
      summary: "Baseline left Scalar's external proxy enabled, omitted an IExceptionHandler and OpenAPI Bearer scheme, and called db.Database.MigrateAsync() inside Program.cs.",
      baseline: {
        file: "Program.cs",
        lang: "csharp",
        warnLines: [1, 2, 13, 20],
        code: `builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddAutoMapper(typeof(Program).Assembly);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Orders API").WithTheme(ScalarTheme.Purple);
    });

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}`,
        callouts: [
          "Omits options.ProxyUrl = null, routing local requests and tokens via proxy.scalar.com.",
          "Missing IExceptionHandler and OpenAPI Bearer security scheme transformer.",
          "Calls db.Database.MigrateAsync() inside web app startup."
        ]
      },
      skill: {
        file: "Program.cs",
        lang: "csharp",
        passLines: [1, 4, 6, 19, 20, 22],
        code: `builder.Services.AddValidation();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((doc, _, _) =>
{
    doc.Components ??= new OpenApiComponents();
    doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
    };
    return Task.CompletedTask;
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.ProxyUrl = null).AllowAnonymous();
}
app.MapApiEndpoints();`,
        callouts: [
          "Sets ProxyUrl = null (off external proxy) and .AllowAnonymous() so Scalar works under default-deny FallbackPolicy.",
          "Registers AddValidation(), AddExceptionHandler<GlobalExceptionHandler>(), and Bearer transformer.",
          "Keeps Program.cs clean with MapApiEndpoints() and zero startup MigrateAsync()."
        ]
      }
    },
    {
      id: "efcore",
      label: "EF Core & Testing",
      prompt: "Provide database migration / startup setup, Order entity ID and timestamp generation, and an integration test verifying order creation and retrieval.",
      title: "Out-of-band migrations, Named Query Filters, FakeTimeProvider, and Respawn",
      summary: "Baseline mutated DbContextOptions in WebApplicationFactory, used DateTimeOffset.UtcNow in tests, and omitted Respawn cleanup and tenant query filters.",
      baseline: {
        file: "OrdersApiTests.cs",
        lang: "csharp",
        warnLines: [13, 14, 15, 20],
        code: `public async Task InitializeAsync()
{
    await _postgres.StartAsync();
    await using var scope = Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        var d = services.SingleOrDefault(x => x.ServiceType == typeof(DbContextOptions<AppDbContext>));
        if (d is not null) services.Remove(d);
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
    });
}

// In test body:
var request = new CreateOrderRequest("buyer@acme.test", 149.99m, DateTimeOffset.UtcNow.AddDays(7));`,
        callouts: [
          "Replaces DbContextOptions manually instead of builder.UseSetting(...).",
          "Uses non-deterministic DateTimeOffset.UtcNow instead of FakeTimeProvider.",
          "No Respawn reset between tests; no EF Core tenant query filter."
        ]
      },
      skill: {
        file: "AppDbContext.cs / OrdersApiTests.cs",
        lang: "csharp",
        passLines: [2, 3, 8, 10, 18, 21],
        code: `// Apply migrations out-of-band: dotnet ef migrations bundle --self-contained -o efbundle
modelBuilder.Entity<TEntity>()
    .HasQueryFilter("TenantFilter", e => e.TenantId == CurrentTenantId);

// In OrdersWebApplicationFactory:
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
    builder.UseSetting("JwtAuth:Authority", "https://issuer.test");
    builder.ConfigureServices(s => s.AddSingleton<TimeProvider>(fakeTime));
}

[TestClass]
[DoNotParallelize]
public sealed class OrdersApiTests
{
    [TestInitialize]
    public async Task TestInit() => await _respawner.ResetAsync(conn);

    // Asserts deterministic timestamp via FakeTimeProvider + MSTest Assert.AreEqual
    Assert.AreEqual(_fakeTime.GetUtcNow(), created.CreatedAtUtc);
}`,
        callouts: [
          "Runs app migrations via efbundle / SQL scripts, never web startup Migrate().",
          "Uses EF Core 10 Named Query Filter (TenantFilter) and AuditSaveChangesInterceptor.",
          "Preserves ValidateOnStart() via builder.UseSetting(...) and resets PostgreSQL 18 with Respawn."
        ]
      }
    },
    {
      id: "workers",
      label: "Workers & Config",
      prompt: "Create an OrderCleanupOptions configuration class and a BackgroundService (OrderCleanupWorker) that runs on a recurring interval and cleans up expired orders using AppDbContext.",
      title: "[OptionsValidator], IOptionsMonitor<T>, and [LoggerMessage]",
      summary: "Baseline used reflection ValidateDataAnnotations(), read static IOptions<T> inside a singleton BackgroundService, and allocated on logger calls.",
      baseline: {
        file: "Program.cs / OrderCleanupWorker.cs",
        lang: "csharp",
        warnLines: [4, 9, 15, 22],
        code: `builder.Services
    .AddOptions<OrderCleanupOptions>()
    .BindConfiguration(OrderCleanupOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

public sealed class OrderCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OrderCleanupOptions> options,
    TimeProvider timeProvider,
    ILogger<OrderCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deleted = await db.Orders.Where(o => o.ExpiresAtUtc <= timeProvider.GetUtcNow())
                .Take(options.Value.BatchSize).ExecuteDeleteAsync(stoppingToken);
            logger.LogInformation("Deleted {DeletedCount} expired pending orders.", deleted);
        }
    }
}`,
        callouts: [
          "Uses reflection ValidateDataAnnotations() instead of AOT-safe [OptionsValidator].",
          "Injects static IOptions<T> into singleton worker; config changes never reload.",
          "Standard logger.LogInformation boxes value types on every tick."
        ]
      },
      skill: {
        file: "OrderCleanupOptions.cs / OrderCleanupWorker.cs",
        lang: "csharp",
        passLines: [1, 6, 12, 17, 24],
        code: `[OptionsValidator]
public sealed partial class OrderCleanupOptionsValidator : IValidateOptions<OrderCleanupOptions>;

public sealed partial class OrderCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<OrderCleanupOptions> options,
    TimeProvider timeProvider,
    ILogger<OrderCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CurrentValue.Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deleted = await db.Orders.IgnoreQueryFilters(["TenantFilter"])
                .Where(o => o.ExpiresAtUtc <= timeProvider.GetUtcNow())
                .Take(options.CurrentValue.BatchSize).ExecuteDeleteAsync(stoppingToken);
            if (deleted > 0) LogExpiredOrdersDeleted(deleted);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired orders.")]
    private partial void LogExpiredOrdersDeleted(int count);
}`,
        callouts: [
          "Compile-time [OptionsValidator] + AddOptionsWithValidateOnStart<T, TValidator>().",
          "Singleton-safe IOptionsMonitor<T> + selective .IgnoreQueryFilters([\"TenantFilter\"]).",
          "Zero-allocation [LoggerMessage] source generation."
        ]
      }
    },
    {
      id: "auth",
      label: "Auth & Permissions",
      prompt: "Configure JWT Bearer authentication in Program.cs and secure DELETE /api/orders/{id} for authorized admins while enforcing tenant/organization ownership.",
      title: "Permission RBAC + FallbackPolicy vs. RequireRole(\"Admin\")",
      summary: "Baseline authorized by role name (RequireRole(\"Admin\")), omitted FallbackPolicy and ClockSkew tightening, and queried orders without a tenant filter.",
      baseline: {
        file: "Program.cs / OrderEndpoints.cs",
        lang: "csharp",
        warnLines: [4, 11, 15, 20],
        code: `builder.Services.AddAuthorizationBuilder()
    .AddPolicy("OrdersAdmin", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("Admin")
        .RequireClaim("tenant_id"));

group.MapDelete("/{id:guid}", async (
    Guid id, ClaimsPrincipal user, AppDbContext dbContext,
    IAuthorizationService authorizationService, CancellationToken ct) =>
{
    var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
    if (order is null) return TypedResults.NotFound();

    var authResult = await authorizationService.AuthorizeAsync(user, order, "OrderTenantOwner");
    if (!authResult.Succeeded) return TypedResults.Forbid();

    dbContext.Orders.Remove(order);
    await dbContext.SaveChangesAsync(ct);
    return TypedResults.NoContent();
}).RequireAuthorization("OrdersAdmin");`,
        callouts: [
          "Checks hardcoded role name RequireRole(\"Admin\") instead of fine-grained permissions.",
          "No default-deny FallbackPolicy; default 5-minute JWT ClockSkew.",
          "Unscoped query leaks cross-tenant order existence via 403 Forbid vs 404 NotFound."
        ]
      },
      skill: {
        file: "Program.cs / OrderEndpoints.cs",
        lang: "csharp",
        passLines: [2, 6, 7, 9, 12, 17],
        code: `builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtAuthOptions>>((jwt, cfg) => {
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = new() { NameClaimType = "sub", ClockSkew = TimeSpan.FromSeconds(30) };
    });

orders.MapDelete("/{id:guid}", DeleteOrder)
    .RequirePermission(Permissions.OrdersDelete);

private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteOrder(
    Guid id, AppDbContext db, IAuthorizationService auth, ClaimsPrincipal user, CancellationToken ct)
{
    var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct); // TenantFilter active
    if (order is null) return TypedResults.NotFound();
    var result = await auth.AuthorizeAsync(user, order, Operations.Delete);
    if (!result.Succeeded) return TypedResults.Forbid();
    db.Orders.Remove(order);
    await db.SaveChangesAsync(ct);
    return TypedResults.NoContent();
}`,
        callouts: [
          "Enforces default-deny FallbackPolicy, MapInboundClaims = false, and 30s ClockSkew.",
          "Dynamic PermissionPolicyProvider checks orders:delete instead of role names.",
          "TenantResolutionMiddleware + TenantFilter + TenantSaveChangesInterceptor prevent BOLA/IDOR."
        ]
      }
    }
  ],

  scorecard: [
    {
      rule: "Time-ordered UUIDv7 keys via Guid.CreateVersion7()",
      baseline: true,
      skill: true,
      evidence: "Both generated Guid.CreateVersion7() keys on Order"
    },
    {
      rule: "PeriodicTimer(..., timeProvider) + IServiceScopeFactory in BackgroundService",
      baseline: true,
      skill: true,
      evidence: "Both used PeriodicTimer with TimeProvider and CreateAsyncScope()"
    },
    {
      rule: "No deprecated .WithOpenApi() (ASPDEPR002) or Swashbuckle",
      baseline: true,
      skill: true,
      evidence: "Both used built-in AddOpenApi() + Scalar.AspNetCore"
    },
    {
      rule: "Avoid commercialized packages (MediatR 13+, AutoMapper 15+, FluentAssertions 8+)",
      baseline: false,
      skill: true,
      evidence: "Baseline added MediatR, AutoMapper, FluentAssertions; Skill used direct handlers + static From/Project + MSTest"
    },
    {
      rule: "Scalar ProxyUrl = null + OpenAPI Bearer security scheme transformer",
      baseline: false,
      skill: true,
      evidence: "Baseline omitted ProxyUrl = null and Bearer transformer; Skill configured both"
    },
    {
      rule: "Global IExceptionHandler + AddValidation() + MapApiEndpoints() hub",
      baseline: false,
      skill: true,
      evidence: "Skill wired GlobalExceptionHandler, AddValidation(), and MapApiEndpoints()"
    },
    {
      rule: "No web-startup db.Database.MigrateAsync() races",
      baseline: false,
      skill: true,
      evidence: "Baseline called db.Database.MigrateAsync() in Program.cs; Skill used efbundle out-of-band"
    },
    {
      rule: "MSTest.Sdk on Microsoft.Testing.Platform + Respawn + FakeTimeProvider",
      baseline: false,
      skill: true,
      evidence: "Baseline used DateTimeOffset.UtcNow and omitted Respawn; Skill used MTP + Respawn + FakeTimeProvider"
    },
    {
      rule: "Compile-time [OptionsValidator] + singleton-safe IOptionsMonitor<T>",
      baseline: false,
      skill: true,
      evidence: "Baseline used reflection ValidateDataAnnotations() and static IOptions<T>"
    },
    {
      rule: "Zero-allocation [LoggerMessage] source generation",
      baseline: false,
      skill: true,
      evidence: "Baseline used boxing logger.LogInformation; Skill used [LoggerMessage]"
    },
    {
      rule: "Permission-based RBAC (.RequirePermission) + default-deny FallbackPolicy",
      baseline: false,
      skill: true,
      evidence: "Baseline used RequireRole(\"Admin\") with no FallbackPolicy; Skill used PermissionPolicyProvider"
    },
    {
      rule: "TenantFilter + TenantSaveChangesInterceptor + BOLA/IDOR defense-in-depth",
      baseline: false,
      skill: true,
      evidence: "Baseline leaked cross-tenant existence via 403 Forbid; Skill scoped queries with Named Query Filter"
    }
  ],

  catalog: [
    { skill: "dotnet", name: "csharp.md", scope: "C# 14 idioms, primary constructors, pattern matching, ValueTask vs Task, AOT JSON source generation", path: "skills/dotnet/references/csharp.md" },
    { skill: "dotnet", name: "api.md", scope: "Minimal APIs, OpenAPI transformers, ProblemDetails handlers, upload streaming, API versioning", path: "skills/dotnet/references/api.md" },
    { skill: "dotnet", name: "architecture.md", scope: "Vertical slice design, CQRS boundaries, compile-time mapping with Riok.Mapperly", path: "skills/dotnet/references/architecture.md" },
    { skill: "dotnet", name: "configuration.md", scope: "IOptions<T> with [OptionsValidator] and ValidateOnStart(), env var binding, User Secrets, feature flags", path: "skills/dotnet/references/configuration.md" },
    { skill: "dotnet", name: "caching.md", scope: "HybridCache coherence, stampede protection, [ImmutableObject(true)], tag eviction, Output Caching", path: "skills/dotnet/references/caching.md" },
    { skill: "dotnet", name: "resilience.md", scope: "AddStandardResilienceHandler, non-HTTP ResiliencePipeline, timeout budgets, Retry-After handling", path: "skills/dotnet/references/resilience.md" },
    { skill: "dotnet", name: "concurrency.md", scope: "SemaphoreSlim and keyed locks, thread pool safety, Parallel.ForEachAsync bounds, advisory locks", path: "skills/dotnet/references/concurrency.md" },
    { skill: "dotnet", name: "database-migrations.md", scope: "Expand and Contract schema changes, lock-safe DDL (CONCURRENTLY, NOT VALID), idempotent scripts", path: "skills/dotnet/references/database-migrations.md" },
    { skill: "dotnet", name: "messaging.md", scope: "Native EF Core and WolverineFx Transactional Outbox, pinned MassTransit v8, idempotent consumers", path: "skills/dotnet/references/messaging.md" },
    { skill: "dotnet", name: "multi-tenancy.md", scope: "Tenant isolation, EF Core Global Query Filters, SaveChangesInterceptor enforcement, tenant cache keys", path: "skills/dotnet/references/multi-tenancy.md" },
    { skill: "dotnet", name: "observability.md", scope: "OpenTelemetry tracing and metrics, [LoggerMessage], data redaction, health probes, container defaults", path: "skills/dotnet/references/observability.md" },
    { skill: "dotnet", name: "hosted-services.md", scope: "BackgroundService loops, IServiceScopeFactory, PeriodicTimer, bounded channels, graceful shutdown", path: "skills/dotnet/references/hosted-services.md" },
    { skill: "dotnet", name: "efcore.md", scope: "EF Core query tuning, split queries, complex types, global query filters, Guid.CreateVersion7()", path: "skills/dotnet/references/efcore.md" },
    { skill: "dotnet", name: "performance.md", scope: "Allocation tracking, Span<T> and Memory<T>, BenchmarkDotNet rules", path: "skills/dotnet/references/performance.md" },
    { skill: "dotnet", name: "security.md", scope: "Identity and passkeys, JsonWebTokenHandler, default-deny policies, AddRateLimiter, CORS, HMAC", path: "skills/dotnet/references/security.md" },
    { skill: "dotnet", name: "authorization.md", scope: "Tenant-scoped RBAC (<resource>:<action>), PermissionPolicyProvider, BOLA/IDOR, ReBAC escalation", path: "skills/dotnet/references/authorization.md" },
    { skill: "dotnet", name: "testing.md", scope: "MSTest.Sdk on Microsoft.Testing.Platform, xUnit v3, Testcontainers + Respawn, FakeTimeProvider", path: "skills/dotnet/references/testing.md" },
    { skill: "dotnet", name: "review.md", scope: "Code review checklist with structured findings (severity, location, finding, evidence, fix)", path: "skills/dotnet/references/review.md" },

    { skill: "fastendpoints", name: "endpoint.md", scope: "Class-per-endpoint Endpoint<TReq, TRes>, route groups, [FromClaim], Send*Async, Native AOT", path: "skills/fastendpoints/references/endpoint.md" },
    { skill: "fastendpoints", name: "validation.md", scope: "FluentValidation via Validator<TRequest>, automatic 400 Problem Details, error accumulation", path: "skills/fastendpoints/references/validation.md" },
    { skill: "fastendpoints", name: "security.md", scope: "Auth defaults, AllowAnonymous(), policies, permissions, claims binding, endpoint throttling", path: "skills/fastendpoints/references/security.md" },
    { skill: "fastendpoints", name: "messaging.md", scope: "In-process Command Bus (ICommand<TResult>), Event Bus, background Job Queues, Server-Sent Events", path: "skills/fastendpoints/references/messaging.md" },
    { skill: "fastendpoints", name: "testing.md", scope: "Integration testing with FastEndpoints.Testing, AppFixture, generic POSTAsync and GETAsync clients", path: "skills/fastendpoints/references/testing.md" },

    { skill: "dotnet-scaffold", name: "SKILL.md", scope: "Greenfield intake (auth first), architecture detection (VSA/CA/Modular Monolith), 9-point checklist", path: "skills/dotnet-scaffold/SKILL.md" },
    { skill: "dotnet-scaffold", name: "scripts/scaffold.cs", scope: "Cross-platform C# runner (dotnet run scaffold.cs) targeting .NET 10 LTS and .slnx solutions", path: "skills/dotnet-scaffold/scripts/scaffold.cs" },
    { skill: "dotnet-scaffold", name: "assets/", scope: "Directory.Packages.props (CPM), Directory.Build.props, global.json, TestProject.csproj, SliceTemplate", path: "skills/dotnet-scaffold/assets" }
  ]
};
