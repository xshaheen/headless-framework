// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace HeadlessShop.Ordering.Domain;

public sealed class Order : AggregateRoot<Guid>, IMultiTenant
{
    private Order()
    {
        TenantId = string.Empty;
    }

    public string TenantId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Order Place(Guid id, string tenantId, Guid productId, int quantity)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Order id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product id must not be empty.", nameof(productId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be greater than zero.");
        }

        return new()
        {
            Id = id,
            TenantId = tenantId,
            ProductId = productId,
            Quantity = quantity,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
