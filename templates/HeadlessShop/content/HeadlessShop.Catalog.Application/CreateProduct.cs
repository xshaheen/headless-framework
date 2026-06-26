using Headless.Messaging;
using HeadlessShop.Catalog.Domain;
using HeadlessShop.Catalog.Infrastructure;
using HeadlessShop.Contracts;

namespace HeadlessShop.Catalog.Application;

public sealed record CreateProduct(string Sku, string Name, decimal Price) : ICommand<ProductView>;

public sealed class CreateProductValidator : AbstractValidator<CreateProduct>
{
    public CreateProductValidator()
    {
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Price).GreaterThan(0);
    }
}

public sealed class CreateProductHandler(CatalogDbContext dbContext, ICurrentTenant currentTenant, IBus bus)
    : ICommandHandler<CreateProduct, ProductView>
{
    public async ValueTask<ProductView> Handle(CreateProduct command, CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("CreateProduct requires an authenticated tenant context.");
        }

        var duplicateSku = await dbContext.Products.AnyAsync(product => product.Sku == command.Sku, cancellationToken);

        if (duplicateSku)
        {
            throw new InvalidOperationException($"Product SKU '{command.Sku}' already exists for this tenant.");
        }

        var product = Product.Create(Guid.NewGuid(), tenantId, command.Sku, command.Name, command.Price);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        await bus.PublishAsync(
            new ProductCreated(product.Id, product.Sku, product.Name, product.Price, tenantId),
            cancellationToken: cancellationToken
        );

        return new(product.Id, product.Sku, product.Name, product.Price);
    }
}
