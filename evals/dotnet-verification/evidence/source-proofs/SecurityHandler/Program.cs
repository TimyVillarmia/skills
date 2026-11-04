using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using System.Security.Claims;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthorization(options =>
{
    // Require authentication by default on all endpoints
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSingleton<IAuthorizationHandler,OrderAuthorizationHandler>();
await using var provider=builder.Services.BuildServiceProvider();
var auth=provider.GetRequiredService<IAuthorizationService>();
var owner=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"owner")],"test"));
var stranger=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"other")],"test"));
if(!(await auth.AuthorizeAsync(owner,new Order("owner"),Operations.Read)).Succeeded)throw new Exception("owner denied");
if((await auth.AuthorizeAsync(stranger,new Order("owner"),Operations.Read)).Succeeded)throw new Exception("stranger admitted");
Console.WriteLine("Security: handler allows owner and denies stranger");
public static class Operations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
}

public sealed class OrderAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, Order>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Order resource)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is not null && resource.CustomerId == userId)
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

public sealed record Order(string CustomerId);
