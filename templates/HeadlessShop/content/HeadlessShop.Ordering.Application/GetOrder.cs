using HeadlessShop.Ordering.Infrastructure;

namespace HeadlessShop.Ordering.Application;

public sealed record GetOrder(Guid OrderId) : IQuery<OrderView?>;

public sealed class GetOrderHandler(OrderingDbContext dbContext) : IQueryHandler<GetOrder, OrderView?>
{
    public async ValueTask<OrderView?> Handle(GetOrder query, CancellationToken cancellationToken)
    {
        return await dbContext
            .Orders.Where(order => order.Id == query.OrderId)
            .Select(order => new OrderView(order.Id, order.ProductId, order.Quantity))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
