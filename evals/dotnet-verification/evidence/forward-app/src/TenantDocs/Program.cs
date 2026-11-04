using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantDocs;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
builder.Services.AddOptions<StoreOptions>().Bind(builder.Configuration.GetSection("Store"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Store:ConnectionString is required.").ValidateOnStart();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "TenantDocs.Auth";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().RequireAssertion(context => context.User.FindAll("tenant_id").Count() == 1 &&
        Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out var id) && id != Guid.Empty).Build());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddDbContext<DocumentsDb>((services, options) =>
    options.UseSqlServer(services.GetRequiredService<IOptions<StoreOptions>>().Value.ConnectionString));
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<MaintenancePass>();
builder.Services.AddHostedService<MaintenanceWorker>();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapDocumentEndpoints();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.Run();
public partial class Program;
