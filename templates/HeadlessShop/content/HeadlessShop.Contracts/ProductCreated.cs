namespace HeadlessShop.Contracts;

public sealed record ProductCreated(Guid ProductId, string Sku, string Name, decimal Price, string TenantId);
