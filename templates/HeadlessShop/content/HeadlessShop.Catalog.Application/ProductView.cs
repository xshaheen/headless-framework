namespace HeadlessShop.Catalog.Application;

public sealed record ProductView(Guid Id, string Sku, string Name, decimal Price);
