// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using HeadlessShop.Ordering.Domain;
using HeadlessShop.Ordering.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HeadlessShop.Ordering.Application;

public sealed record PlaceOrderCommand(Guid ProductId, int Quantity) : ICommand<OrderDto>;

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0);
    }
}

public sealed class PlaceOrderCommandHandler(OrderingDbContext dbContext, ICurrentTenant currentTenant)
    : ICommandHandler<PlaceOrderCommand, OrderDto>
{
    public async ValueTask<OrderDto> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("PlaceOrder requires an authenticated tenant context.");
        }

        var productExists = await dbContext.ProductSnapshots.AnyAsync(
            product => product.Id == command.ProductId,
            cancellationToken
        );

        if (!productExists)
        {
            throw new InvalidOperationException("The product must exist in Ordering before an order can be placed.");
        }

        var order = Order.Place(Guid.NewGuid(), tenantId, command.ProductId, command.Quantity);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new(order.Id, order.ProductId, order.Quantity);
    }
}
