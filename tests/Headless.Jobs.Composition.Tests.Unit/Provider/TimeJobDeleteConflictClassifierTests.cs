// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests.Provider;

public sealed class TimeJobDeleteConflictClassifierTests : TestBase
{
    private const string _PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string _SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    [Theory]
    [InlineData("23503")]
    [InlineData("40001")]
    [InlineData("40P01")]
    public void should_retry_postgresql_tree_delete_conflicts(string sqlState)
    {
        var exception = new FakeDbException(sqlState: sqlState);

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(_PostgreSqlProvider, exception, commitStarted: false, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public void should_not_retry_postgresql_unique_constraint_violation()
    {
        var exception = new FakeDbException(sqlState: "23505");

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(_PostgreSqlProvider, exception, commitStarted: false, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(547)]
    [InlineData(1205)]
    [InlineData(3960)]
    public void should_retry_sql_server_tree_delete_conflicts(int number)
    {
        var exception = new FakeDbException(number: number);

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(_SqlServerProvider, exception, commitStarted: false, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public void should_not_retry_sql_server_unique_constraint_violation()
    {
        var exception = new FakeDbException(number: 2627);

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(_SqlServerProvider, exception, commitStarted: false, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public void should_retry_transient_database_failure_for_any_provider()
    {
        var exception = new FakeDbException(isTransient: true);

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure("Unknown.Provider", exception, commitStarted: false, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(_PostgreSqlProvider)]
    [InlineData(_SqlServerProvider)]
    [InlineData("Unknown.Provider")]
    public void should_not_retry_operation_cancellation(string providerName)
    {
        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(
            providerName,
            new OperationCanceledException(),
            commitStarted: false,
            CancellationToken.None
        );

        result.Should().BeFalse();
    }

    [Fact]
    public void should_not_retry_when_caller_cancellation_is_already_requested()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(
            _PostgreSqlProvider,
            new FakeDbException(sqlState: "23503"),
            commitStarted: false,
            cancellation.Token
        );

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(_PostgreSqlProvider)]
    [InlineData(_SqlServerProvider)]
    [InlineData("Unknown.Provider")]
    public void should_not_retry_after_commit_starts(string providerName)
    {
        var exception = new FakeDbException(sqlState: "23503", number: 547, isTransient: true);

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(providerName, exception, commitStarted: true, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(_PostgreSqlProvider)]
    [InlineData(_SqlServerProvider)]
    [InlineData("Unknown.Provider")]
    public void should_not_retry_invalid_operation_failure(string providerName)
    {
        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(
            providerName,
            new InvalidOperationException(),
            commitStarted: false,
            CancellationToken.None
        );

        result.Should().BeFalse();
    }

    [Fact]
    public void should_unwrap_matching_database_failure()
    {
        var exception = new DbUpdateException("delete failed", new FakeDbException(sqlState: "40P01"));

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure(_PostgreSqlProvider, exception, commitStarted: false, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public void should_not_retry_non_transient_database_failure_for_unknown_provider()
    {
        var exception = new FakeDbException(sqlState: "23503", number: 547);

        var result = JobsEfCorePersistenceProvider<
            DbContext,
            TimeJobEntity,
            CronJobEntity
        >.IsRetryableTreeDeleteFailure("Unknown.Provider", exception, commitStarted: false, CancellationToken.None);

        result.Should().BeFalse();
    }

    private sealed class FakeDbException(string? sqlState = null, int number = 0, bool isTransient = false)
        : DbException
    {
        public override string? SqlState { get; } = sqlState;

        public override bool IsTransient { get; } = isTransient;

        public int Number { get; } = number;
    }
}
