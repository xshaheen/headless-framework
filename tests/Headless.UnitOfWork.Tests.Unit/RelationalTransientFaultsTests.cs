// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork.Internal;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

public sealed class RelationalTransientFaultsTests : TestBase
{
    [Fact]
    public void should_classify_a_driver_reported_transient_failure_as_transient()
    {
        RelationalTransientFaults
            .IsTransient(new FakeDbException(isTransient: true), CancellationToken.None)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public void should_classify_serialization_failures_and_deadlocks_as_transient(string sqlState)
    {
        RelationalTransientFaults
            .IsTransient(new FakeDbException(sqlState: sqlState), CancellationToken.None)
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("23503")]
    [InlineData("23505")]
    [InlineData(null)]
    public void should_not_classify_constraint_violations_as_transient(string? sqlState)
    {
        RelationalTransientFaults
            .IsTransient(new FakeDbException(sqlState: sqlState), CancellationToken.None)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(1205, true)]
    [InlineData(3960, true)]
    [InlineData(547, false)]
    [InlineData(2627, false)]
    public void should_classify_sql_server_deadlocks_and_snapshot_conflicts_by_error_number(int number, bool expected)
    {
        var exception = new Microsoft.Data.SqlClient.SqlException(number);

        RelationalTransientFaults.IsTransient(exception, CancellationToken.None).Should().Be(expected);
    }

    [Fact]
    public void should_not_read_sql_server_error_numbers_from_another_driver()
    {
        // MySQL reports a lock-wait timeout as 1205: an error number means nothing outside its own driver.
        RelationalTransientFaults
            .IsTransient(new FakeDbException(number: 1205), CancellationToken.None)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void should_read_the_outer_driver_failure_when_it_wraps_a_socket_error()
    {
        var exception = new FakeDbException(isTransient: true, inner: new IOException("connection reset"));

        RelationalTransientFaults.IsTransient(exception, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void should_stop_at_the_first_driver_failure_in_the_chain()
    {
        var exception = new InvalidOperationException(
            "save failed",
            new FakeDbException(sqlState: "23505", inner: new FakeDbException(isTransient: true))
        );

        RelationalTransientFaults.IsTransient(exception, CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public void should_unwrap_a_non_driver_wrapper()
    {
        var exception = new InvalidOperationException("save failed", new FakeDbException(sqlState: "40001"));

        RelationalTransientFaults.IsTransient(exception, CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void should_not_classify_a_failure_without_a_driver_exception_as_transient()
    {
        RelationalTransientFaults
            .IsTransient(new IOException("connection reset"), CancellationToken.None)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void should_not_classify_a_cancellation_as_transient()
    {
        RelationalTransientFaults
            .IsTransient(new OperationCanceledException(), CancellationToken.None)
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_not_classify_a_failure_observed_after_the_caller_cancelled_as_transient()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        RelationalTransientFaults
            .IsTransient(new FakeDbException(isTransient: true), cancellation.Token)
            .Should()
            .BeFalse();
    }

    private sealed class FakeDbException(
        string? sqlState = null,
        int number = 0,
        bool isTransient = false,
        Exception? inner = null
    ) : DbException("fake database failure", inner)
    {
        public override string? SqlState { get; } = sqlState;

        public override bool IsTransient { get; } = isTransient;

        public int Number { get; } = number;
    }
}
