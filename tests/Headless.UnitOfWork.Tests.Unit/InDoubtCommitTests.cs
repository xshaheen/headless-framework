// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Net.Sockets;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// A commit fault is in-doubt when the commit may have reached the database but no answer came back, and a definite
/// failure when the database answered with an error or nothing was sent. The factory reports the first as
/// <see cref="UnitOfWorkFailureReason.InDoubt" /> and throws <see cref="UnitOfWorkInDoubtException" />.
/// </summary>
public sealed class InDoubtCommitTests : TestBase
{
    private static readonly Dictionary<string, (Exception Fault, bool InDoubt)> _Classifications = new(
        StringComparer.Ordinal
    )
    {
        ["driver fault over a socket error"] = (new FakeDbException(inner: new SocketException()), true),
        ["driver fault over an I/O error"] = (new FakeDbException(inner: new IOException("reset")), true),
        ["driver timeout"] = (new FakeDbException(inner: new TimeoutException()), true),
        ["bare I/O error"] = (new IOException("broken pipe"), true),
        ["transient driver fault with no server state"] = (new FakeDbException(isTransient: true), true),
        ["connection exception (08006)"] = (new FakeDbException(sqlState: "08006"), true),
        ["server terminated the connection (57P01)"] = (new FakeDbException(sqlState: "57P01"), true),
        ["SqlClient timeout (-2)"] = (new Microsoft.Data.SqlClient.SqlException(-2, severity: 11), true),
        ["SqlClient connection-closing severity"] = (
            new Microsoft.Data.SqlClient.SqlException(10054, severity: 20),
            true
        ),
        ["serialization failure answered by the server (40001)"] = (new FakeDbException(sqlState: "40001"), false),
        ["deferred constraint answered by the server (23505)"] = (new FakeDbException(sqlState: "23505"), false),
        ["SqlClient server error"] = (new Microsoft.Data.SqlClient.SqlException(3960, severity: 16), false),
        ["non-transient driver fault with no server state"] = (new FakeDbException(), false),
        ["client-side fault before the send"] = (new InvalidOperationException("transaction completed"), false),
        ["wrapped server error"] = (
            new InvalidOperationException("commit failed", new FakeDbException(sqlState: "23505")),
            false
        ),
    };

    public static TheoryData<string> Classifications => [.. _Classifications.Keys];

    [Theory]
    [MemberData(nameof(Classifications))]
    public void should_classify_a_commit_fault_by_whether_the_database_could_have_committed(string scenario)
    {
        var (fault, inDoubt) = _Classifications[scenario];

        InDoubtCommitFaults.IsInDoubt(fault, cancelledBeforeCommit: false).Should().Be(inDoubt, scenario);
    }

    [Fact]
    public void should_classify_a_cancellation_during_the_commit_as_in_doubt()
    {
        InDoubtCommitFaults.IsInDoubt(new OperationCanceledException(), cancelledBeforeCommit: false).Should().BeTrue();
    }

    [Fact]
    public void should_classify_a_cancellation_requested_before_the_commit_as_a_definite_failure()
    {
        InDoubtCommitFaults.IsInDoubt(new OperationCanceledException(), cancelledBeforeCommit: true).Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_the_unit_as_in_doubt_and_throw_the_in_doubt_exception_when_the_connection_drops_during_the_commit()
    {
        var logger = new CapturingLogger<UnitOfWorkFactory>();
        var factory = new UnitOfWorkFactory(logger);
        var driverFault = new FakeDbException(isTransient: true, inner: new IOException("connection reset"));
        var resource = new FakeUnitOfWorkResource { CommitFault = driverFault };
        var unit = await factory.BeginAsync(_ => ValueTask.FromResult<IUnitOfWorkResource>(resource), AbortToken);
        UnitOfWorkFailure? observed = null;
        var completed = false;
        unit.OnFailed(failure =>
        {
            observed = failure;

            return ValueTask.CompletedTask;
        });
        unit.OnCompleted(() =>
        {
            completed = true;

            return ValueTask.CompletedTask;
        });

        var act = () => unit.CompleteAsync(AbortToken).AsTask();

        var thrown = (await act.Should().ThrowAsync<UnitOfWorkInDoubtException>()).Which;
        thrown.InnerException.Should().BeSameAs(driverFault);
        thrown.Should().BeAssignableTo<InvalidOperationException>();
        unit.State.Should().Be(UnitOfWorkState.Failed);
        unit.Failure!.Reason.Should().Be(UnitOfWorkFailureReason.InDoubt);
        unit.Failure.Exception.Should().BeSameAs(thrown);
        observed.Should().BeSameAs(unit.Failure);
        completed.Should().BeFalse("OnCompleted never runs for a commit whose outcome is unknown");
        logger.Entries.Should().ContainSingle(entry => entry.EventId.Id == 7 && entry.Level == LogLevel.Warning);

        var second = () => unit.CompleteAsync(AbortToken).AsTask();
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already failed (InDoubt)*");
    }

    [Fact]
    public async Task should_fail_the_unit_as_faulted_and_rethrow_the_fault_when_the_database_answers_the_commit_with_an_error()
    {
        var logger = new CapturingLogger<UnitOfWorkFactory>();
        var factory = new UnitOfWorkFactory(logger);
        var serverError = new FakeDbException(sqlState: "40001");
        var resource = new FakeUnitOfWorkResource { CommitFault = serverError };
        var unit = await factory.BeginAsync(_ => ValueTask.FromResult<IUnitOfWorkResource>(resource), AbortToken);

        var act = () => unit.CompleteAsync(AbortToken).AsTask();

        (await act.Should().ThrowAsync<FakeDbException>()).Which.Should().BeSameAs(serverError);
        unit.Failure!.Reason.Should().Be(UnitOfWorkFailureReason.Faulted);
        unit.Failure.Exception.Should().BeSameAs(serverError);
        logger.Entries.Should().NotContain(entry => entry.EventId.Id == 7);
    }

    [Fact]
    public async Task should_surface_an_in_doubt_commit_from_run_without_replaying_it()
    {
        var factory = new UnitOfWorkFactory();
        var attempts = 0;
        var replayEverything = new Polly.Retry.RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.Zero,
            ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Exception is not null),
        };

        var act = () =>
            UnitOfWorkRunner.RunPerAttemptConnectionAsync(
                factory,
                _ => ValueTask.FromResult(new DisposableProbe()),
                (_, ct) =>
                    factory.BeginAsync(
                        _ =>
                            ValueTask.FromResult<IUnitOfWorkResource>(
                                new FakeUnitOfWorkResource
                                {
                                    CommitFault = new FakeDbException(inner: new SocketException()),
                                }
                            ),
                        ct
                    ),
                (_, _, _) =>
                {
                    attempts++;

                    return Task.FromResult(true);
                },
                replayEverything,
                AbortToken
            );

        await act.Should().ThrowAsync<UnitOfWorkInDoubtException>();
        attempts.Should().Be(1, "an in-doubt commit may already be durable, so it is never replayed");
    }

    private sealed class DisposableProbe : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDbException(string? sqlState = null, bool isTransient = false, Exception? inner = null)
        : DbException("fake database failure", inner)
    {
        public override string? SqlState { get; } = sqlState;

        public override bool IsTransient { get; } = isTransient;
    }
}
