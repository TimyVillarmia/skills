using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SliceTemplate;

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orders")
            .WithTags("Orders");

        group.MapPost("/", async Task<Results<Created<OrderResponse>, ProblemHttpResult>> (
            CreateOrderRequest request,
            CreateOrderHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);
            return result.Match<Results<Created<OrderResponse>, ProblemHttpResult>>(
                order => TypedResults.Created($"/orders/{order.Id}", OrderResponse.From(order)),
                error => TypedResults.Problem(
                    statusCode: error.Type == ErrorType.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                    detail: error.Description,
                    title: error.Code));
        })
        .WithName("CreateOrder");

        return group;
    }
}
