// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Messaging;
using HeadlessShop.Catalog.Domain;
using HeadlessShop.Catalog.Infrastructure;
using HeadlessShop.Contracts;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HeadlessShop.Catalog.Application;

public sealed record CreateProductCommand(string Sku, string Name, decimal Price) : ICommand<ProductDto>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Price).GreaterThan(0);
    }
}

public sealed class CreateProductCommandHandler(
    CatalogDbContext dbContext,
    ICurrentTenant currentTenant,
    IDirectPublisher publisher
) : ICommandHandler<CreateProductCommand, ProductDto>
{
    public async ValueTask<ProductDto> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("CreateProduct requires an authenticated tenant context.");
        }

        var duplicateSku = await dbContext.Products.AnyAsync(
            product => product.Sku == command.Sku,
            cancellationToken
        );

        if (duplicateSku)
        {
            throw new InvalidOperationException($"Product SKU '{command.Sku}' already exists for this tenant.");
        }

        var product = Product.Create(Guid.NewGuid(), tenantId, command.Sku, command.Name, command.Price);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        await publisher.PublishAsync(
            new ProductCreated(product.Id, product.Sku, product.Name, product.Price, tenantId),
            cancellationToken
        );

        return new(product.Id, product.Sku, product.Name, product.Price);
    }
}
