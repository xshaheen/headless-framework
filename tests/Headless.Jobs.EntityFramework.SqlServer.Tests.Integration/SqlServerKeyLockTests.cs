// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests;

[Collection<JobsKeyLockSqlServerFixture>]
public sealed class SqlServerKeyLockTests(JobsKeyLockSqlServerFixture fixture)
    : JobsKeyLockConformanceTests(options => options.UseSqlServer(fixture.ConnectionString))
{
    protected override string CountLocksSql =>
        "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE request_session_id = @@SPID AND resource_type = 'APPLICATION'";
    protected override string TryLockSql =>
        """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=@key, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0;
            SELECT CAST(CASE WHEN @result >= 0 THEN 1 ELSE 0 END AS bit);
            """;
    protected override string ReadLockTimeoutSql => "SELECT @@LOCK_TIMEOUT";
    protected override string SetLockTimeoutSql => "SET LOCK_TIMEOUT 7000";
    protected override string CreateProbeTableSql => "CREATE TABLE #jobs_key_lock_probe (id int)";
    protected override string ProbeTable => "#jobs_key_lock_probe";

    protected override object LockResource(byte[] digest) => "jobs:key:" + Convert.ToHexStringLower(digest);

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
}
