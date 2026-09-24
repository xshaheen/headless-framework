// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.SqlServer;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqlServerDistributedLockFixture>]
public sealed class SqlServerUnitOfWorkAdvisoryLockTests(SqlServerDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_hold_the_lock_inside_the_unit_and_release_it_on_complete()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, cancellationToken: AbortToken);

        // when
        await unit.AdvisoryLocks.AcquireAsync(resource, AbortToken);

        // then — a transaction lock on another connection contends while the unit holds it
        (await _TryContendAsync(resource))
            .Should()
            .BeFalse();

        await unit.CompleteAsync(AbortToken);

        (await _TryContendAsync(resource)).Should().BeTrue();
    }

    [Fact]
    public async Task should_try_acquire_false_while_another_unit_holds_it_and_true_after_it_completes()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();

        await using var holderConnection = new SqlConnection(fixture.ConnectionString);
        await using var contenderConnection = new SqlConnection(fixture.ConnectionString);

        await using var holder = await factory.BeginAsync(holderConnection, cancellationToken: AbortToken);
        await holder.AdvisoryLocks.AcquireAsync(resource, AbortToken);

        // when / then
        await using var contender = await factory.BeginAsync(contenderConnection, cancellationToken: AbortToken);
        (await contender.AdvisoryLocks.TryAcquireAsync(resource, AbortToken)).Should().BeFalse();

        await holder.CompleteAsync(AbortToken);

        (await contender.AdvisoryLocks.TryAcquireAsync(resource, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_release_the_lock_when_the_unit_rolls_back()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var resource = _CreateResourceName();

        await using var connection = new SqlConnection(fixture.ConnectionString);

        // when — dispose without complete is an implicit rollback
        await using (var unit = await factory.BeginAsync(connection, cancellationToken: AbortToken))
        {
            await unit.AdvisoryLocks.AcquireAsync(resource, AbortToken);
            (await _TryContendAsync(resource)).Should().BeFalse();
        }

        // then
        (await _TryContendAsync(resource))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_refuse_a_unit_with_no_relational_resource()
    {
        // given
        await using var provider = _BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        // when
        var act = async () => await unit.AdvisoryLocks.AcquireAsync(_CreateResourceName(), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no relational resource*");
    }

    private ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup => setup.UseSqlServer(fixture.ConnectionString));
        services.AddSqlServerUnitOfWork();

        return services.BuildServiceProvider();
    }

    private static string _CreateResourceName()
    {
        return "uow-advisory-lock-tests:" + Guid.NewGuid();
    }

    private async Task<bool> _TryContendAsync(string resource)
    {
        // The static helper encodes KeyPrefix + resource exactly as the feature does, so both address one app lock.
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        var acquired = await SqlServerDistributedLock.TryAcquireWithTransactionAsync(
            resource,
            transaction,
            TimeSpan.Zero,
            cancellationToken: AbortToken
        );

        await transaction.RollbackAsync(AbortToken);

        return acquired;
    }
}
