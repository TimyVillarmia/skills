using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Scalar.AspNetCore;
using SliceTemplate;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IUserQueries,UserQueries>();
builder.Services.AddAuthorization();
  builder.Services.AddOpenApi();
  builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
        ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/users/{id}", async Task<Results<Ok<UserDto>, NotFound>> (
    Guid id,
    IUserQueries queries,
    CancellationToken cancellationToken) =>
{
    var user = await queries.GetAsync(id, cancellationToken);
    return user is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(user);
});
app.MapUserEndpoints();
  if (app.Environment.IsDevelopment())
  {
      app.MapOpenApi();
      app.MapScalarApiReference(); // Accessible at /scalar/v1
  }
  
Console.WriteLine("API examples registration constructed");
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/{id:guid}", GetUserById);
        group.MapPost("/", CreateUser);

        return group;
    }

    private static IResult CreateUser() => TypedResults.Ok();

    private static async Task<Results<Ok<UserDto>, NotFound>> GetUserById(
        Guid id,
        IUserQueries queries,
        CancellationToken ct)
    {
        var user = await queries.GetAsync(id, ct);
        return user is not null ? TypedResults.Ok(user) : TypedResults.NotFound();
    }
}

public sealed class GetUserHandler(HybridCache cache, AppDbContext db)
{
    public async Task<UserResponse?> HandleAsync(Guid id, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            $"user-{id}",
            async token =>
            {
                var user = await db.Users.FindAsync([id], token);
                return user is null ? null : UserResponse.From(user);
            },
            tags: ["users"],
            cancellationToken: ct);
    }
}

public record UserDto(Guid Id);
public interface IUserQueries { Task<UserDto?> GetAsync(Guid id,CancellationToken ct); }
public sealed class UserQueries : IUserQueries { public Task<UserDto?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult<UserDto?>(null); }
public sealed class User { public Guid Id {get;set;} }
public sealed class AppDbContext(DbContextOptions<AppDbContext> options):DbContext(options) { public DbSet<User> Users=>Set<User>(); }
public record UserResponse(Guid Id) { public static UserResponse From(User u)=>new(u.Id); }
public static class ResultMapper {
 public static Results<Ok<UserResponse>,NotFound,Conflict,ProblemHttpResult> Map(Result<User> result) {
return result.Match<Results<Ok<UserResponse>, NotFound, Conflict, ProblemHttpResult>>(
    success => TypedResults.Ok(UserResponse.From(success)),
    error => error.Type switch
    {
        ErrorType.NotFound => TypedResults.NotFound(),
        ErrorType.Conflict => TypedResults.Conflict(),
        _ => TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            detail: error.Description,
            title: error.Code)
    });
 } }

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        var problemDetails = new ProblemDetails
        {
            Status = exception switch
            {
                BadHttpRequestException => StatusCodes.Status400BadRequest,
                UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status500InternalServerError
            },
            Title = exception switch
            {
                BadHttpRequestException => "Bad Request",
                UnauthorizedAccessException => "Unauthorized",
                _ => "Internal Server Error"
            },
            Detail = httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred."
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
