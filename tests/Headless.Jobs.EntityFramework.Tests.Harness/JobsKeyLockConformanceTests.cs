// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests;

public abstract class JobsKeyLockConformanceTests(Action<DbContextOptionsBuilder> configureStore) : TestBase
{
    protected abstract string CountLocksSql { get; }
    protected abstract string TryLockSql { get; }
    protected abstract string ReadLockTimeoutSql { get; }
    protected abstract string SetLockTimeoutSql { get; }
    protected abstract string CreateProbeTableSql { get; }
    protected abstract string ProbeTable { get; }
    protected abstract object LockResource(byte[] digest);

    public virtual async Task bulk_locks_every_distinct_run_until_the_caller_ends_its_transaction(bool commit)
    {
        // Exceed SQL Server's 2,100-parameter limit and exercise the complete bulk payload.
        var ids = Enumerable.Range(1, 2200).Select(i => new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ToArray();
        await using var context = _CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(AbortToken);
        await JobsKeyLock.AcquireRunsAsync(context, Enumerable.Reverse(ids).Concat(ids), AbortToken);
        context.Database.CurrentTransaction.Should().BeSameAs(transaction);
        (await _ScalarAsync(context, CountLocksSql)).Should().Be(ids.Length);
        foreach (var id in new[] { ids[0], ids[1000], ids[^1] })
        {
            (await _CanAcquireRunAsync(id)).Should().BeFalse();
        }

        if (commit)
        {
            await transaction.CommitAsync(AbortToken);
        }
        else
        {
            await transaction.RollbackAsync(AbortToken);
        }

        await using var next = _CreateContext();
        await using var nextTransaction = await next.Database.BeginTransactionAsync(AbortToken);
        await JobsKeyLock.AcquireRunsAsync(next, ids, AbortToken);
        (await _ScalarAsync(next, CountLocksSql)).Should().Be(ids.Length);
    }

    public virtual async Task contention_stops_at_the_first_unavailable_run_in_sorted_order()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }.Order().ToArray();
        await using var blocker = _CreateContext();
        await using var blockingTransaction = await blocker.Database.BeginTransactionAsync(AbortToken);
        await JobsKeyLock.AcquireRunsAsync(blocker, [ids[1]], AbortToken);
        await using var waiter = _CreateContext();
        await using var waitingTransaction = await waiter.Database.BeginTransactionAsync(AbortToken);
        await Task.WhenAll(acquireAsync(), observeAsync());

        (await _ScalarAsync(waiter, CountLocksSql)).Should().Be(3);

        async Task acquireAsync() => await JobsKeyLock.AcquireRunsAsync(waiter, Enumerable.Reverse(ids), AbortToken);

        async Task observeAsync()
        {
            try
            {
                var elapsed = Stopwatch.StartNew();
                while (await _CanAcquireRunAsync(ids[0]))
                {
                    elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
                    await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
                }

                (await _CanAcquireRunAsync(ids[2])).Should().BeTrue();
            }
            finally
            {
                await blockingTransaction.RollbackAsync(AbortToken);
            }
        }
    }

    public virtual async Task contention_timeout_preserves_caller_work_and_timeout_policy(bool keyed)
    {
        var id = Guid.NewGuid();
        var scope = new JobKeyScope("lock-timeout");
        var key = new JobKey(Guid.NewGuid().ToString("N"));
        await using var blocker = _CreateContext();
        await using var blockingTransaction = await blocker.Database.BeginTransactionAsync(AbortToken);
        await _AcquireAsync(blocker);

        await using var waiter = _CreateContext();
        await waiter.Database.OpenConnectionAsync(AbortToken);
        await using var waitingTransaction = await waiter.Database.BeginTransactionAsync(AbortToken);
        await _ScalarAsync(waiter, SetLockTimeoutSql);
        var policy = await _ScalarAsync(waiter, ReadLockTimeoutSql);
        // A lock owned before the helper call must survive an ordinary contention timeout.
        var previousId = Guid.NewGuid();
        await JobsKeyLock.AcquireRunsAsync(waiter, [previousId], AbortToken);
        await _ScalarAsync(waiter, CreateProbeTableSql);
        await _ScalarAsync(waiter, $"INSERT INTO {ProbeTable} (id) VALUES (1)");

        var elapsed = Stopwatch.StartNew();
        var acquire = async () => await _AcquireAsync(waiter);
        await acquire.Should().ThrowExactlyAsync<TimeoutException>();
        elapsed.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(29));
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(40));
        waiter.Database.CurrentTransaction.Should().BeSameAs(waitingTransaction);
        (await _ScalarAsync(waiter, ReadLockTimeoutSql)).Should().Be(policy);
        (await _CanAcquireRunAsync(previousId)).Should().BeFalse();
        await _ScalarAsync(waiter, $"INSERT INTO {ProbeTable} (id) VALUES (2)");
        await waitingTransaction.CommitAsync(AbortToken);
        Convert
            .ToInt32(
                await _ScalarAsync(waiter, $"SELECT COUNT(*) FROM {ProbeTable}"),
                System.Globalization.CultureInfo.InvariantCulture
            )
            .Should()
            .Be(2);
        await _ScalarAsync(waiter, $"DROP TABLE {ProbeTable}");

        Task _AcquireAsync(DbContext context) =>
            keyed
                ? JobsKeyLock.AcquireAsync(context, scope, key, AbortToken)
                : JobsKeyLock.AcquireRunsAsync(context, [id], AbortToken);
    }

    public virtual async Task batch_shares_one_timeout_budget_across_contended_runs()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() }.Order().ToArray();
        await using var first = _CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync(AbortToken);
        await JobsKeyLock.AcquireRunsAsync(first, [ids[0]], AbortToken);
        await using var second = _CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync(AbortToken);
        await JobsKeyLock.AcquireRunsAsync(second, [ids[1]], AbortToken);
        await using var waiter = _CreateContext();
        await using var waitingTransaction = await waiter.Database.BeginTransactionAsync(AbortToken);

        var elapsed = Stopwatch.StartNew();
        await Task.WhenAll(assertTimeoutAsync(), releaseFirstAsync());
        elapsed.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(29));
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(37));
        (await _ScalarAsync(waiter, CountLocksSql)).Should().Be(1);

        async Task assertTimeoutAsync()
        {
            var acquire = async () => await JobsKeyLock.AcquireRunsAsync(waiter, Enumerable.Reverse(ids), AbortToken);
            await acquire.Should().ThrowExactlyAsync<TimeoutException>();
        }

        async Task releaseFirstAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(10), AbortToken);
            await firstTransaction.RollbackAsync(AbortToken);
        }
    }

    private DbContext _CreateContext()
    {
        var options = new DbContextOptionsBuilder();
        configureStore(options);
        return new DbContext(options.Options);
    }

    private static async Task<object?> _ScalarAsync(DbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task<bool> _CanAcquireRunAsync(Guid id)
    {
        await using var context = _CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(AbortToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = TryLockSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@key";
        parameter.Value = LockResource(SHA256.HashData(Encoding.UTF8.GetBytes("jobs:run:" + id.ToString("D"))));
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }
}
