// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public abstract partial class JobsTransactionalKeyedConformanceTests<TFixture>
{
    private const string _RecurringExpression = "0 0 3 * * *";

    public virtual Task required_recurring_definition_rejects_scheduling_outside_a_transaction() =>
        _WithHostAsync(async host =>
        {
            var schedule = () => _ScheduleRecurringAsync(host.Services.GetRequiredService<IJobScheduler>(), AbortToken);
            await schedule.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires a unit of work*");
            (await fixture.CountCronJobsAsync(AbortToken)).Should().Be(0);
        });

    public virtual Task required_recurring_definition_shares_the_outer_commit_or_rollback(bool commit) =>
        _WithHostAsync(async host =>
        {
            Guid definitionId = default;
            var operation = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, unitOfWork, connection, transaction, ct) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                        definitionId = await _ScheduleRecurringAsync(unitOfWork.Jobs, ct);
                        // The definition row must not have escaped the caller's transaction: subsequent caller SQL
                        // still runs on the same live transaction.
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                        if (!commit)
                        {
                            throw new InjectedFailureException();
                        }
                    },
                    AbortToken
                );
            if (commit)
            {
                await operation();
            }
            else
            {
                await operation.Should().ThrowAsync<InjectedFailureException>();
            }
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(commit ? 2 : 0);
            (await fixture.CountCronJobsAsync(AbortToken)).Should().Be(commit ? 1 : 0);
            var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var persisted = await store.GetCronJobByIdAsync(definitionId, AbortToken);
            if (!commit)
            {
                persisted.Should().BeNull();
                return;
            }
            persisted.Should().NotBeNull();
            persisted!.Expression.Should().Be(_RecurringExpression);
            // The requirement is call intent, never a materialized definition attribute.
            persisted.Enlistment.Should().Be(TransactionEnlistment.Optional);
        });

    public virtual Task recurring_atomic_flag_is_not_mapped_to_a_column() =>
        _WithHostAsync(async host =>
        {
            await using var context = await _ContextAsync(host);
            context
                .Model.FindEntityType(typeof(CronJobEntity))!
                .FindProperty(nameof(CronJobEntity.Enlistment))
                .Should()
                .BeNull();
        });

    private static Task<Guid> _ScheduleRecurringAsync(IJobScheduler scheduler, CancellationToken cancellationToken)
    {
        return scheduler.ScheduleRecurringAsync(
            new CoordinatedFacadeRequest(Guid.Empty, "recurring"),
            _RecurringExpression,
            new RecurringJobOptions { Enlistment = TransactionEnlistment.Required },
            cancellationToken
        );
    }
}
