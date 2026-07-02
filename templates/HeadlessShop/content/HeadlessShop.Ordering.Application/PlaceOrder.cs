using HeadlessShop.Ordering.Domain;
using HeadlessShop.Ordering.Infrastructure;

namespace HeadlessShop.Ordering.Application;

public sealed record PlaceOrder(Guid ProductId, int Quantity) : ICommand<OrderView>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0);
    }
}

public sealed class PlaceOrderHandler(OrderingDbContext dbContext, ICurrentTenant currentTenant)
    : ICommandHandler<PlaceOrder, OrderView>
{
    public async ValueTask<OrderView> Handle(PlaceOrder command, CancellationToken cancellationToken)
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
