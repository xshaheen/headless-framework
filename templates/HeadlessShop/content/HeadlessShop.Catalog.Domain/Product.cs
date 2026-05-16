// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace HeadlessShop.Catalog.Domain;

public sealed class Product : AggregateRoot<Guid>, IMultiTenant
{
    private Product()
    {
        Sku = string.Empty;
        Name = string.Empty;
        TenantId = string.Empty;
    }

    public string TenantId { get; private set; }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public decimal Price { get; private set; }

    public static Product Create(Guid id, string tenantId, string sku, string name, decimal price)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Product id must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("SKU is required.", nameof(sku));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "Price must be greater than zero.");
        }

        return new()
        {
            Id = id,
            TenantId = tenantId,
            Sku = sku.Trim(),
            Name = name.Trim(),
            Price = price,
        };
    }
}
