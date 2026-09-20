// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Messaging;
using Headless.UnitOfWork;
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

    [Fact]
    public async Task should_replay_a_run_async_block_whose_enlisted_publish_preceded_a_transient_failure()
    {
        // The counterpart to the pipeline-owned case above: RunAsync owns the unit, so a replay re-runs the whole
        // block — the publish included — and the enlisted write must not end replay. The first attempt's row rolls
        // back with its transaction; the second attempt writes it again, once.
        var key = $"run-async-replay-{Guid.NewGuid():N}";
        var attempts = 0;
        await using var provider = await _BuildProviderAsync(configureDbContext: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryOnceStrategyFactory>()
        );
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var unitOfWorkFactory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        var order = new OrderEntity { Name = key };

        await unitOfWorkFactory.RunAsync(
            db,
            async (unit, ct) =>
            {
                attempts++;
                db.Orders.Add(order);
                await unit.Outbox.PublishAsync(new OrderShipped(key), ct);

                if (attempts == 1)
                {
                    throw new TransientOutboxException();
                }

                await db.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
        );

        attempts.Should().Be(2, "the enlisted publish left the owned unit replayable");
        (await _CountPublishedContainingAsync(key)).Should().Be(1);
        (await _CountOrdersAsync()).Should().Be(1);
    }

    private sealed class DirectPublishRetryEvidence
    {
        public string Key { get; } = $"direct-publish-{Guid.NewGuid():N}";
        public bool FailNextOccurrence { get; set; } = true;
        public int Calls { get; set; }
        public int Writes { get; set; }
    }

    private sealed class PublishBeforeTransientFailure(BridgeTestDbContext db, DirectPublishRetryEvidence evidence)
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
                // Enlisted in the save's unit of work: the row joins that transaction, and the write forfeits
                // execution-strategy replay for the rest of the unit — which is what this case pins.
                var unitOfWork =
                    db.UnitOfWork()
                    ?? throw new InvalidOperationException("The save pipeline must have bound a unit to the context.");

                await unitOfWork.Outbox.PublishAsync(new OrderShipped(evidence.Key), cancellationToken);
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
