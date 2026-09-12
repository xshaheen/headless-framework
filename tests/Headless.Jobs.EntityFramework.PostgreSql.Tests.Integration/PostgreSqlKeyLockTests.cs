// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Diagnostics;
using Headless.Jobs.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tests;

[Collection<JobsKeyLockPostgreSqlFixture>]
public sealed class PostgreSqlKeyLockTests(JobsKeyLockPostgreSqlFixture fixture)
    : JobsKeyLockConformanceTests(options => options.UseNpgsql(fixture.ConnectionString))
{
    protected override string CountLocksSql =>
        "SELECT count(*)::int FROM pg_locks WHERE pid = pg_backend_pid() AND locktype = 'advisory'";
    protected override string TryLockSql => "SELECT pg_try_advisory_xact_lock(@key)";
    protected override string ReadLockTimeoutSql => "SHOW lock_timeout";
    protected override string SetLockTimeoutSql => "SET LOCAL lock_timeout = '7s'";
    protected override string CreateProbeTableSql => "CREATE TEMP TABLE jobs_key_lock_probe (id int)";
    protected override string ProbeTable => "jobs_key_lock_probe";

    protected override object LockResource(byte[] digest) => BinaryPrimitives.ReadInt64LittleEndian(digest);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task bulk_locks_every_distinct_run_until_the_caller_ends_its_transaction(bool commit) =>
        base.bulk_locks_every_distinct_run_until_the_caller_ends_its_transaction(commit);

    [Fact]
    public override Task contention_stops_at_the_first_unavailable_run_in_sorted_order() =>
        base.contention_stops_at_the_first_unavailable_run_in_sorted_order();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task contention_timeout_preserves_caller_work_and_timeout_policy(bool keyed) =>
        base.contention_timeout_preserves_caller_work_and_timeout_policy(keyed);

    [Fact]
    public override Task batch_shares_one_timeout_budget_across_contended_runs() =>
        base.batch_shares_one_timeout_budget_across_contended_runs();

    [Fact]
    public async Task contended_batch_sleeps_on_the_server()
    {
        var options = new DbContextOptionsBuilder().UseNpgsql(fixture.ConnectionString).Options;
        await using var blocker = new DbContext(options);
        await using var blockingTransaction = await blocker.Database.BeginTransactionAsync(AbortToken);
        var id = Guid.NewGuid();
        await JobsKeyLock.AcquireRunsAsync(blocker, [id], AbortToken);
        await using var waiter = new DbContext(options);
        await using var waitingTransaction = await waiter.Database.BeginTransactionAsync(AbortToken);
        var pid = ((NpgsqlConnection)waiter.Database.GetDbConnection()).ProcessID;
        await Task.WhenAll(acquireAsync(), observeAsync());

        async Task acquireAsync() => await JobsKeyLock.AcquireRunsAsync(waiter, [id], AbortToken);

        async Task observeAsync()
        {
            try
            {
                await using var observer = new NpgsqlConnection(fixture.ConnectionString);
                await observer.OpenAsync(AbortToken);
                await using var command = observer.CreateCommand();
                command.CommandText = "SELECT wait_event FROM pg_stat_activity WHERE pid = @pid";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@pid";
                parameter.Value = pid;
                command.Parameters.Add(parameter);
                var elapsed = Stopwatch.StartNew();
                while (!Equals(await command.ExecuteScalarAsync(AbortToken), "PgSleep"))
                {
                    elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
                    await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
                }
            }
            finally
            {
                await blockingTransaction.RollbackAsync(AbortToken);
            }
        }
    }
}
