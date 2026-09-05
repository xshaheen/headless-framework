// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

public sealed partial class OutboxBridgeIntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task should_require_fresh_unit_of_work_after_coordinated_local_write_fails(
        bool synchronous,
        bool failDuringDrain
    )
    {
        var fault = new OutboxFault { FailuresRemaining = failDuringDrain ? 0 : 1 };
        var evidence = new CoordinatedRetryEvidence { FailDuringDrain = failDuringDrain };
        await using var provider = await _BuildProviderAsync(
            services =>
            {
                services.RemoveAll<IDomainEventHandler<OrderShipping>>();
                services.AddSingleton(evidence);
                services.AddScoped<IDomainEventHandler<OrderShipping>, ScheduleLocalDeadline>();
                _AddFaultingDispatcher(services, fault);
            },
            options => options.ReplaceService<IExecutionStrategyFactory, RetryOnceStrategyFactory>(),
            includeJobs: true
        );
        await _CreateJobsSchemaAsync(provider);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
            var order = new OrderEntity { Name = evidence.Key };
            order.EmitShipping();
            if (failDuringDrain)
            {
                order.EmitShipping();
            }
            db.Orders.Add(order);

            var save = () => _SaveAsync(db, synchronous);
            await save.Should().ThrowAsync<TransientOutboxException>();
        }

        evidence.Calls.Should().Be(failDuringDrain ? 2 : 1);
        evidence.Writes.Should().Be(1);
        fault.Attempts.Should().HaveCount(failDuringDrain ? 0 : 1);
        (await _ReadDeadlineRowsAsync(provider, evidence.Key)).Should().BeEmpty();
        (await _CountOrdersAsync()).Should().Be(0);
        (await _CountPublishedContainingAsync(evidence.Key)).Should().Be(0);

        // Recovery reconstructs both context and graph; it cannot reuse the rolled-back handler's checkpoint.
        evidence.Calls = 0;
        evidence.FailDuringDrain = false;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
            var order = new OrderEntity { Name = evidence.Key };
            order.EmitShipping();
            db.Orders.Add(order);
            await _SaveAsync(db, synchronous);
        }

        evidence.Writes.Should().Be(2);
        (await _ReadDeadlineRowsAsync(provider, evidence.Key)).Should().ContainSingle();
        (await _CountOrdersAsync()).Should().Be(1);
        (await _CountPublishedContainingAsync(evidence.Key)).Should().Be(1);
    }

    private sealed class CoordinatedRetryEvidence
    {
        public string Key { get; } = $"local-deadline-{Guid.NewGuid():N}";
        public DateTimeOffset Due { get; } = DateTimeOffset.UtcNow.AddHours(1);
        public bool FailDuringDrain { get; set; }
        public int Calls { get; set; }
        public int Writes { get; set; }
    }

    private sealed class ScheduleLocalDeadline(IJobScheduler scheduler, CoordinatedRetryEvidence evidence)
        : IDomainEventHandler<OrderShipping>
    {
        public async ValueTask HandleAsync(
            EventContext<OrderShipping> context,
            CancellationToken cancellationToken = default
        )
        {
            evidence.Calls++;
            if (evidence.FailDuringDrain && evidence.Calls == 2)
            {
                throw new TransientOutboxException();
            }
            if (evidence.Calls != 1)
            {
                return;
            }

            await scheduler.ScheduleKeyedAsync(
                new JobKey(evidence.Key),
                DeadlineRegistration.Descriptor,
                evidence.Due,
                new JobOptions { RequireAtomicEnlistment = true },
                cancellationToken
            );
            evidence.Writes++;
            context.Payload.Order.EmitIntegrationEvent(new OrderShipped(evidence.Key));
        }
    }
}
