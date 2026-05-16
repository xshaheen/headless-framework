// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace HeadlessShop.Ordering.Domain;

public sealed class ProductSnapshot : Entity<Guid>, IMultiTenant
{
    private ProductSnapshot()
    {
        TenantId = string.Empty;
        Sku = string.Empty;
        Name = string.Empty;
    }

    private ProductSnapshot(Guid id, string tenantId, string sku, string name, decimal price)
    {
        Id = id;
        TenantId = tenantId;
        Sku = sku;
        Name = name;
        Price = price;
    }

    public string TenantId { get; private set; }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public decimal Price { get; private set; }

    public static ProductSnapshot Create(Guid id, string tenantId, string sku, string name, decimal price)
    {
        return new(id, tenantId, sku, name, price);
    }

    public bool Update(string sku, string name, decimal price)
    {
        if (
            string.Equals(Sku, sku, StringComparison.Ordinal)
            && string.Equals(Name, name, StringComparison.Ordinal)
            && Price == price
        )
        {
            return false;
        }

        Sku = sku;
        Name = name;
        Price = price;

        return true;
    }
}
