// Copyright (c) Mahmoud Shaheen. All rights reserved.

using HeadlessShop.Catalog.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HeadlessShop.Catalog.Application;

public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductDto?>;

public sealed class GetProductQueryHandler(CatalogDbContext dbContext) : IQueryHandler<GetProductQuery, ProductDto?>
{
    public async ValueTask<ProductDto?> Handle(GetProductQuery query, CancellationToken cancellationToken)
    {
        return await dbContext
            .Products.Where(product => product.Id == query.ProductId)
            .Select(product => new ProductDto(product.Id, product.Sku, product.Name, product.Price))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
