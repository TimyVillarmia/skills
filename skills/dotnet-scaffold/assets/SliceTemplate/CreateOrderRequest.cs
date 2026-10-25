namespace SliceTemplate;

public sealed record CreateOrderRequest(string CustomerId, decimal TotalAmount);
