using Headless.Exceptions;
using Headless.Primitives;
using HeadlessShop.Catalog.Domain;
using HeadlessShop.Catalog.Infrastructure;
using Npgsql;

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

public sealed class CreateProductHandler(CatalogDbContext dbContext, ICurrentTenant currentTenant)
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
            throw _DuplicateSku(command.Sku);
        }

        var product = Product.Create(Guid.NewGuid(), tenantId, command.Sku, command.Name, command.Price);
        dbContext.Products.Add(product);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw _DuplicateSku(command.Sku);
        }

        return new(product.Id, product.Sku, product.Name, product.Price);
    }

    private static ConflictException _DuplicateSku(string sku)
    {
        var error = new ErrorDescriptor("catalog:duplicate_sku", $"Product SKU '{sku}' already exists.");
        return new ConflictException(error);
    }
}
