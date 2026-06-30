// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.SqlServer;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqlServerApplicationLockTests : TestBase
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void should_map_non_negative_getapplock_results_to_acquired(int result)
    {
        SqlServerApplicationLock.MapAcquireResult("resource", result, TimeSpan.Zero, AbortToken).Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(103)]
    public void should_map_timeout_and_reentrant_results_to_not_acquired(int result)
    {
        SqlServerApplicationLock
            .MapAcquireResult("resource", result, TimeSpan.FromSeconds(1), AbortToken)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void should_reject_reentrant_infinite_wait()
    {
        var act = () =>
            SqlServerApplicationLock.MapAcquireResult("resource", 103, Timeout.InfiniteTimeSpan, AbortToken);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_map_cancel_result_to_operation_canceled()
    {
        var act = () => SqlServerApplicationLock.MapAcquireResult("resource", -2, TimeSpan.FromSeconds(1), AbortToken);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void should_map_deadlock_result_to_distributed_lock_deadlock_exception()
    {
        var act = () => SqlServerApplicationLock.MapAcquireResult("resource", -3, TimeSpan.FromSeconds(1), AbortToken);

        act.Should().Throw<DistributedLockDeadlockException>().Which.Resource.Should().Be("resource");
    }

    [Fact]
    public void should_map_parameter_error_to_argument_exception()
    {
        var act = () =>
            SqlServerApplicationLock.MapAcquireResult("resource", -999, TimeSpan.FromSeconds(1), AbortToken);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_update_mode_result_with_invalid_operation_exception()
    {
        var act = () => SqlServerApplicationLock.MapAcquireResult("resource", 104, TimeSpan.FromSeconds(1), AbortToken);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_use_infinite_command_timeout_for_infinite_lock_wait()
    {
        var timeout = SqlServerApplicationLock.GetCommandTimeoutSeconds(
            Timeout.InfiniteTimeSpan,
            TimeSpan.FromSeconds(30)
        );

        timeout.Should().Be(0);
    }

    [Fact]
    public void should_not_let_command_timeout_undercut_finite_lock_wait()
    {
        var timeout = SqlServerApplicationLock.GetCommandTimeoutSeconds(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(30)
        );

        timeout.Should().BeGreaterThan(120);
    }
}
