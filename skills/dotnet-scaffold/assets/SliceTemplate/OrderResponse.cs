namespace SliceTemplate;

public sealed record OrderResponse(Guid Id, string CustomerId, decimal TotalAmount)
{
    public static OrderResponse From(Order order) =>
        new(order.Id, order.CustomerId, order.TotalAmount);
}

public sealed record Order(Guid Id, string CustomerId, decimal TotalAmount);
