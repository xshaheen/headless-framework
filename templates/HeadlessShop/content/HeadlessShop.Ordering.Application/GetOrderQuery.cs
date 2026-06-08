// Copyright (c) Mahmoud Shaheen. All rights reserved.

using HeadlessShop.Ordering.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HeadlessShop.Ordering.Application;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto?>;

public sealed class GetOrderQueryHandler(OrderingDbContext dbContext) : IQueryHandler<GetOrderQuery, OrderDto?>
{
    public async ValueTask<OrderDto?> Handle(GetOrderQuery query, CancellationToken cancellationToken)
    {
        return await dbContext
            .Orders.Where(order => order.Id == query.OrderId)
            .Select(order => new OrderDto(order.Id, order.ProductId, order.Quantity))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
