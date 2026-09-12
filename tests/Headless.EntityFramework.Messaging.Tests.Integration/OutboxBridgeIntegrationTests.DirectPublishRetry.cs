// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

public sealed partial class OutboxBridgeIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_require_fresh_unit_of_work_when_direct_publish_succeeds_before_transient_domain_failure(
        bool synchronous
    )
    {
        var evidence = new DirectPublishRetryEvidence();
        await using var provider = await _BuildProviderAsync(
            services =>
            {
                services.RemoveAll<IDomainEventHandler<OrderShipping>>();
                services.AddSingleton(evidence);
                services.AddScoped<IDomainEventHandler<OrderShipping>, PublishBeforeTransientFailure>();
            },
            options => options.ReplaceService<IExecutionStrategyFactory, RetryOnceStrategyFactory>()
        );

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
            var order = new OrderEntity { Name = evidence.Key };
            order.EmitShipping();
            order.EmitShipping();
            db.Orders.Add(order);

            var save = async () => await _SaveAsync(db, synchronous);
            await save.Should().ThrowAsync<TransientOutboxException>();
        }

        evidence.Calls.Should().Be(2);
        evidence.Writes.Should().Be(1);
        (await _CountPublishedContainingAsync(evidence.Key)).Should().Be(0);
        (await _CountOrdersAsync()).Should().Be(0);

        // Recovery must rerun the publishing occurrence in a new context and aggregate graph.
        evidence.Calls = 0;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
            var order = new OrderEntity { Name = evidence.Key };
            order.EmitShipping();
            order.EmitShipping();
            db.Orders.Add(order);
            await _SaveAsync(db, synchronous);
        }

        evidence.Calls.Should().Be(2);
        evidence.Writes.Should().Be(2);
        (await _CountPublishedContainingAsync(evidence.Key)).Should().Be(1);
        (await _CountOrdersAsync()).Should().Be(1);
    }

    private sealed class DirectPublishRetryEvidence
    {
        public string Key { get; } = $"direct-publish-{Guid.NewGuid():N}";
        public bool FailNextOccurrence { get; set; } = true;
        public int Calls { get; set; }
        public int Writes { get; set; }
    }

    private sealed class PublishBeforeTransientFailure(IBus bus, DirectPublishRetryEvidence evidence)
        : IDomainEventHandler<OrderShipping>
    {
        public async ValueTask HandleAsync(
            EventContext<OrderShipping> context,
            CancellationToken cancellationToken = default
        )
        {
            evidence.Calls++;
            if (evidence.Calls == 1)
            {
                await bus.PublishAsync(
                    new OrderShipped(evidence.Key),
                    new PublishOptions { DeliveryMode = DeliveryMode.Durable },
                    cancellationToken
                );
                evidence.Writes++;
            }
            else if (evidence.FailNextOccurrence)
            {
                evidence.FailNextOccurrence = false;
                throw new TransientOutboxException();
            }
        }
    }
}
