using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace SliceTemplate;

public sealed class OrderEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/orders", async Task<Results<Created<OrderResponse>, ProblemHttpResult>> (
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
    }
}
