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

    // The injected scheduler never enlists: a definition it writes while a unit is open is its own commit, so the
    // unit's rollback discards the unit's domain write and leaves the definition in place.
    public virtual Task recurring_definition_through_the_injected_scheduler_survives_the_outer_rollback() =>
        _WithHostAsync(async host =>
        {
            var operation = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, _, connection, transaction, ct) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                        await _ScheduleRecurringAsync(host.Services.GetRequiredService<IJobScheduler>(), ct);
                        throw new InjectedFailureException();
                    },
                    AbortToken
                );

            await operation.Should().ThrowAsync<InjectedFailureException>();
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0);
            (await fixture.CountCronJobsAsync(AbortToken)).Should().Be(1);
        });

    public virtual Task enlisted_recurring_definition_shares_the_outer_commit_or_rollback(bool commit) =>
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
        });

    private static Task<Guid> _ScheduleRecurringAsync(IJobScheduler scheduler, CancellationToken cancellationToken)
    {
        return scheduler.ScheduleRecurringAsync(
            new CoordinatedFacadeRequest(Guid.Empty, "recurring"),
            _RecurringExpression,
            cancellationToken
        );
    }
}
