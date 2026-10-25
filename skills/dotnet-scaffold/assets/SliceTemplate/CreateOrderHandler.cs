namespace SliceTemplate;

public sealed class CreateOrderHandler
{
    public Task<Result<Order>> HandleAsync(CreateOrderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            return Task.FromResult(Result<Order>.Failure(new AppError("Orders.CustomerRequired", "Customer ID is required.")));
        }

        if (request.TotalAmount <= 0)
        {
            return Task.FromResult(Result<Order>.Failure(new AppError("Orders.InvalidAmount", "Total amount must be greater than zero.")));
        }

        var order = new Order(Guid.NewGuid(), request.CustomerId, request.TotalAmount);
        return Task.FromResult(Result<Order>.Success(order));
    }
}
