using Headless.Domain;

namespace HeadlessShop.Contracts;

public sealed record ProductCreated(
    string UniqueId,
    Guid ProductId,
    string Sku,
    string Name,
    decimal Price,
    string TenantId
) : IIntegrationEvent;
