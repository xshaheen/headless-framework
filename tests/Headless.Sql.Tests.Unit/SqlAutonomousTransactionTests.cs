// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Sql;
using Headless.Testing.Tests;
using Headless.Threading;

namespace Tests;

public sealed class SqlAutonomousTransactionTests : TestBase
{
    [Fact]
    public async Task should_commit_the_attempt_and_return_its_result()
    {
        var connections = new List<FakeConnection>();

        var result = await SqlAutonomousTransaction.RunAsync(
            "test.call",
            () => SqlAutonomousFakes.Track(connections),
            static (_, _, _) => Task.FromResult(42),
            TimeProvider.System,
            AbortToken
        );

        result.Should().Be(42);
        connections.Should().ContainSingle();
        connections[0].Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
        connections[0].Transactions[0].IsolationLevel.Should().Be(IsolationLevel.ReadCommitted);
        connections[0].WasDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_refuse_a_blank_operation_before_opening_a_connection(string operation)
    {
        var connections = new List<FakeConnection>();

        var run = async () =>
            await SqlAutonomousTransaction.RunAsync(
                operation,
                () => SqlAutonomousFakes.Track(connections),
                static (_, _, _) => Task.FromResult(42),
                TimeProvider.System,
                AbortToken
            );
        var retry = async () =>
            await SqlAutonomousTransaction.RetryAsync(
                operation,
                static (_, _) => Task.FromResult(42),
                TimeProvider.System,
                cancellationToken: AbortToken
            );

        await run.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(operation));
        await retry.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(operation));
        connections.Should().BeEmpty();
    }

    public static TheoryData<string> TransientFaults => ["driver-flag", "40001", "40P01"];

    [Theory]
    [MemberData(nameof(TransientFaults))]
    public async Task should_retry_a_transient_fault_from_the_body_on_a_new_connection(string fault)
    {
        var connections = new List<FakeConnection>();
        var attempts = 0;

        var result = await SqlAutonomousTransaction.RunAsync(
            "test.call",
            () => SqlAutonomousFakes.Track(connections),
            (_, _, _) => ++attempts == 1 ? throw SqlAutonomousFakes.Transient(fault) : Task.FromResult(attempts),
            TimeProvider.System,
            AbortToken
        );

        result.Should().Be(2);
        connections
            .Should()
            .HaveCount(2, "a failed attempt's transaction is gone, so the retry starts on a fresh connection");
        connections[0].Transactions[0].Committed.Should().BeFalse();
        connections[0].WasDisposed.Should().BeTrue();
        connections[1].Transactions[0].Committed.Should().BeTrue();
    }

    [Fact]
    public async Task should_retry_a_transient_fault_from_the_transaction_begin()
    {
        var connections = new List<FakeConnection>();

        var result = await SqlAutonomousTransaction.RunAsync(
            "test.call",
            () =>
                SqlAutonomousFakes.Track(
                    connections,
                    beginFault: connections.Count == 0 ? SqlAutonomousFakes.Transient("driver-flag") : null
                ),
            static (_, _, _) => Task.FromResult(7),
            TimeProvider.System,
            AbortToken
        );

        result.Should().Be(7);
        connections.Should().HaveCount(2);
        connections[0].Transactions.Should().BeEmpty();
        connections[1].Transactions[0].Committed.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_retry_a_transient_fault_from_the_commit_and_surface_it_unchanged()
    {
        var connections = new List<FakeConnection>();
        var commitFault = SqlAutonomousFakes.Transient("40001");
        var bodyRuns = 0;

        var act = async () =>
            await SqlAutonomousTransaction.RunAsync(
                "test.call",
                () => SqlAutonomousFakes.Track(connections, commitFault: commitFault),
                (_, _, _) => Task.FromResult(++bodyRuns),
                TimeProvider.System,
                AbortToken
            );

        (await act.Should().ThrowAsync<FakeDbException>()).Which.Should().BeSameAs(commitFault);
        bodyRuns.Should().Be(1, "the commit may have landed on the server, so a retry could apply the call twice");
        connections.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_not_retry_a_non_transient_fault(bool isDatabaseFault)
    {
        var connections = new List<FakeConnection>();
        Exception fault = isDatabaseFault
            ? new FakeDbException(isTransient: false, sqlState: "23505")
            : new InvalidOperationException("not a database fault");

        var act = async () =>
            await SqlAutonomousTransaction.RunAsync<int>(
                "test.call",
                () => SqlAutonomousFakes.Track(connections),
                (_, _, _) => throw fault,
                TimeProvider.System,
                AbortToken
            );

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(fault);
        connections.Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_retry_a_transient_fault_observed_after_the_caller_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var connections = new List<FakeConnection>();
        var fault = SqlAutonomousFakes.Transient("driver-flag");

        var act = async () =>
            await SqlAutonomousTransaction.RunAsync<int>(
                "test.call",
                () => SqlAutonomousFakes.Track(connections),
                async (_, _, _) =>
                {
                    await cancellation.CancelAsync();

                    throw fault;
                },
                TimeProvider.System,
                cancellation.Token
            );

        (await act.Should().ThrowAsync<FakeDbException>()).Which.Should().BeSameAs(fault);
        connections.Should().ContainSingle();
    }

    [Fact]
    public async Task should_give_up_after_the_last_attempt_and_surface_the_fault()
    {
        var connections = new List<FakeConnection>();

        var act = async () =>
            await SqlAutonomousTransaction.RunAsync<int>(
                "test.call",
                () => SqlAutonomousFakes.Track(connections),
                (_, _, _) => throw SqlAutonomousFakes.Transient("40P01"),
                TimeProvider.System,
                AbortToken
            );

        await act.Should().ThrowAsync<FakeDbException>();
        connections.Should().HaveCount(TransientRetry.MaxAttempts);
    }

    [Fact]
    public async Task should_commit_even_when_cancelled_after_the_statements_decided()
    {
        using var cancellation = new CancellationTokenSource();
        var connections = new List<FakeConnection>();

        var result = await SqlAutonomousTransaction.RunAsync(
            "test.call",
            () => SqlAutonomousFakes.Track(connections),
            async (_, _, _) =>
            {
                // The write has been decided; the caller is about to be told it happened.
                await cancellation.CancelAsync();

                return 1;
            },
            TimeProvider.System,
            cancellation.Token
        );

        result.Should().Be(1);
        connections[0].Transactions[0].Committed.Should().BeTrue();
    }

    [Fact]
    public async Task should_report_each_retry_with_the_number_of_the_attempt_about_to_run()
    {
        var retries = new List<(Exception Fault, int AttemptNumber)>();
        var fault = SqlAutonomousFakes.Transient("40001");

        var act = async () =>
            await SqlAutonomousTransaction.RetryAsync<int>(
                "test.call",
                (_, _) => throw fault,
                TimeProvider.System,
                (ex, attemptNumber) => retries.Add((ex, attemptNumber)),
                AbortToken
            );

        await act.Should().ThrowAsync<FakeDbException>();
        retries.Should().Equal((fault, 2), (fault, 3));
    }

    [Fact]
    public async Task should_give_each_attempt_its_own_commit_marker()
    {
        var attempts = new List<SqlAutonomousAttempt>();

        var result = await SqlAutonomousTransaction.RetryAsync(
            "test.call",
            (attempt, _) =>
            {
                attempts.Add(attempt);

                return attempts.Count == 1
                    ? throw SqlAutonomousFakes.Transient("40P01")
                    : Task.FromResult(attempt.CommitStarted);
            },
            TimeProvider.System,
            cancellationToken: AbortToken
        );

        result.Should().BeFalse("a retry starts before its own commit, whatever the previous attempt reached");
        attempts.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task should_not_retry_a_transient_fault_once_the_attempt_marked_its_commit()
    {
        var runs = 0;
        var fault = SqlAutonomousFakes.Transient("driver-flag");

        var act = async () =>
            await SqlAutonomousTransaction.RetryAsync<int>(
                "test.call",
                (attempt, _) =>
                {
                    runs++;
                    attempt.MarkCommitStarted();

                    throw fault;
                },
                TimeProvider.System,
                cancellationToken: AbortToken
            );

        (await act.Should().ThrowAsync<FakeDbException>()).Which.Should().BeSameAs(fault);
        runs.Should().Be(1);
    }
}
