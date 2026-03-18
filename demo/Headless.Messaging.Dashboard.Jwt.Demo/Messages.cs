namespace Demo;

public sealed record OrderCreated
{
    public required int OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal Amount { get; init; }
}

public sealed record PaymentProcessed
{
    public required string PaymentId { get; init; }
    public required int OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed record UserRegistered
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string Plan { get; init; }
}

public sealed record InventoryUpdated
{
    public required string ProductId { get; init; }
    public required int Quantity { get; init; }
    public required string Warehouse { get; init; }
}
