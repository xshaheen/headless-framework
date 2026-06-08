// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using HeadlessShop.Contracts;
using HeadlessShop.Ordering.Domain;
using HeadlessShop.Ordering.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HeadlessShop.Ordering.Modules;

public sealed class ProductCreatedConsumer(OrderingDbContext dbContext, ICurrentTenant currentTenant)
    : IConsume<ProductCreated>
{
    public async ValueTask Consume(ConsumeContext<ProductCreated> context, CancellationToken cancellationToken)
    {
        var tenantId = context.TenantId;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("ProductCreated requires a tenant envelope.");
        }

        using var _ = currentTenant.Change(tenantId);
        var message = context.Message;
        var snapshot = await dbContext.ProductSnapshots.SingleOrDefaultAsync(
            product => product.Id == message.ProductId,
            cancellationToken
        );

        if (snapshot is null)
        {
            dbContext.ProductSnapshots.Add(
                ProductSnapshot.Create(message.ProductId, tenantId, message.Sku, message.Name, message.Price)
            );
        }
        else
        {
            if (!snapshot.Update(message.Sku, message.Name, message.Price))
            {
                return;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
