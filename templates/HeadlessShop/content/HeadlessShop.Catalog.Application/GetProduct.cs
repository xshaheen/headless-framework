using HeadlessShop.Catalog.Infrastructure;

namespace HeadlessShop.Catalog.Application;

public sealed record GetProduct(Guid ProductId) : IQuery<ProductView?>;

public sealed class GetProductHandler(CatalogDbContext dbContext) : IQueryHandler<GetProduct, ProductView?>
{
    public async ValueTask<ProductView?> Handle(GetProduct query, CancellationToken cancellationToken)
    {
        return await dbContext
            .Products.Where(product => product.Id == query.ProductId)
            .Select(product => new ProductView(product.Id, product.Sku, product.Name, product.Price))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
