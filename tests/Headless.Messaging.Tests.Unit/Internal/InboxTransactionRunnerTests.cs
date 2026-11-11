// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Runtime.ExceptionServices;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Internal;

#pragma warning disable CA2000 // False positive: each fake attempt is handed to the runner, which disposes every attempt it begins, and the tests assert that dispose.

/// <summary>
/// The inbox attempt protocol every relational storage shares: the handler runs at most once per reserved attempt,
/// the inbox completion joins the attempt's transaction, and an ambiguous commit is settled by probing the row
/// rather than by running the handler again.
/// </summary>
public sealed class InboxTransactionRunnerTests : TestBase
{
    private readonly List<string> _calls = [];
    private readonly ITransactionalInboxStorage _storage = Substitute.For<ITransactionalInboxStorage>();
    private readonly IDeliveryCoordinationResolver _resolver = Substitute.For<IDeliveryCoordinationResolver>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly DbTransaction _transaction = Substitute.For<DbTransaction>();
    private readonly MediumMessage _message = _CreateMessage();
    private int _handlerEntries;

    public InboxTransactionRunnerTests()
    {
        _resolver.Resolve(_unitOfWork).Returns(DeliveryCoordination.Compatible(_unitOfWork, _transaction));
        _storage
            .CompleteReceivedInboxAsync(_message, _transaction, Arg.Any<CancellationToken>())
            .Returns(_ => _Record("complete", true));
        _unitOfWork.CompleteAsync(Arg.Any<CancellationToken>()).Returns(_ => _RecordVoid("drain"));
        _unitOfWork.RollbackAsync().Returns(_ => _RecordVoid("unit-rollback"));
    }

    [Fact]
    public async Task should_commit_completion_and_drain_when_handler_succeeds()
    {
        // given
        var runner = _CreateRunner();

        // when
        await runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        _handlerEntries.Should().Be(1);
        _calls.Should().Equal("begin", "handler", "flush", "complete", "commit", "drain", "dispose");
    }

    [Fact]
    public async Task should_throw_stale_attempt_without_committing_when_completion_loses_its_fence()
    {
        // given
        _storage
            .CompleteReceivedInboxAsync(_message, _transaction, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        var runner = _CreateRunner();

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        (await act.Should().ThrowAsync<StaleInboxAttemptException>()).WithMessage($"*{_message.StorageId}*");
        _calls.Should().NotContain("commit");
        _calls.Should().EndWith("dispose");
    }

    [Fact]
    public async Task should_report_success_when_commit_faults_but_probe_finds_the_row_committed()
    {
        // given
        var attempt = new FakeAttempt(this) { CommitFault = new TimeoutException("reply lost") };
        _Probe(InboxCommitProbe.Committed);
        var runner = _CreateRunner(attempt);

        // when
        await runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        _handlerEntries.Should().Be(1);
        _calls.Should().Equal("begin", "handler", "flush", "complete", "commit", "probe", "drain", "dispose");
    }

    [Fact]
    public async Task should_throw_uncommitted_after_rolling_back_when_commit_faults_and_probe_is_indeterminate()
    {
        // given
        var commitFault = new TimeoutException("commit failed");
        var attempt = new FakeAttempt(this) { CommitFault = commitFault };
        _Probe(InboxCommitProbe.Indeterminate);
        var runner = _CreateRunner(attempt);

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        (await act.Should().ThrowAsync<UncommittedInboxCommitException>()).WithInnerException<TimeoutException>();
        _calls.Should().ContainInConsecutiveOrder("probe", "rollback", "unit-rollback");
        _calls.Should().NotContain("drain");
    }

    [Fact]
    public async Task should_throw_indeterminate_when_commit_and_rollback_both_fault_and_probe_stays_indeterminate()
    {
        // given
        var attempt = new FakeAttempt(this)
        {
            CommitFault = new TimeoutException("commit failed"),
            RollbackFault = new InvalidOperationException("connection broken"),
        };
        _Probe(InboxCommitProbe.Indeterminate);
        var runner = _CreateRunner(attempt);

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<IndeterminateInboxCommitException>();
        thrown.Which.InnerException.Should().BeOfType<AggregateException>().Which.InnerExceptions.Should().HaveCount(2);
        _calls.Count(call => string.Equals(call, "probe", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public async Task should_report_success_when_rollback_faults_and_second_probe_finds_the_row_committed()
    {
        // given
        var attempt = new FakeAttempt(this)
        {
            CommitFault = new TimeoutException("commit failed"),
            RollbackFault = new InvalidOperationException("connection broken"),
        };
        _storage
            .ProbeReceivedInboxCommitAsync(_message, Arg.Any<CancellationToken>())
            .Returns(
                _ => _Record("probe", InboxCommitProbe.Indeterminate),
                _ => _Record("probe", InboxCommitProbe.Committed)
            );
        var runner = _CreateRunner(attempt);

        // when
        await runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        _calls.Should().ContainInConsecutiveOrder("probe", "rollback", "probe", "drain");
    }

    [Fact]
    public async Task should_report_success_when_drain_faults_after_a_durable_commit()
    {
        // given
        _unitOfWork
            .CompleteAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("drain"));
        _unitOfWork.State.Returns(UnitOfWorkState.Completed);
        var runner = _CreateRunner();

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        await act.Should().NotThrowAsync();
        _calls.Should().Contain("commit");
    }

    [Fact]
    public async Task should_surface_a_handler_fault_after_the_retry_wrapper_without_entering_the_handler_again()
    {
        // given
        var runner = _CreateRunner(retries: 3);

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(new InvalidOperationException("handler")), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler");
        _handlerEntries.Should().Be(1);
        _calls.Should().NotContain("commit");
        _calls.Should().EndWith("dispose");
    }

    [Fact]
    public async Task should_let_the_retry_wrapper_replay_a_fault_raised_before_the_handler_runs()
    {
        // given
        var runner = _CreateRunner(retries: 1, beginFaults: 1);

        // when
        await runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        _handlerEntries.Should().Be(1);
        _calls.Count(call => string.Equals(call, "begin", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public async Task should_refuse_to_run_the_handler_when_the_attempt_cannot_join_the_storage()
    {
        // given
        _resolver
            .Resolve(_unitOfWork)
            .Returns(DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.Database));
        var runner = _CreateRunner();

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<InboxAttemptNotStartedException>();
        thrown.WithInnerException<InvalidOperationException>().WithMessage("*Database*");
        _handlerEntries.Should().Be(0);
    }

    [Fact]
    public async Task should_mark_a_fault_before_the_handler_runs_as_an_attempt_that_never_started()
    {
        // given
        var runner = _CreateRunner(beginFaults: 1);

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<InboxAttemptNotStartedException>();
        thrown.WithMessage($"*{_message.StorageId}*").WithInnerException<TimeoutException>();
        _handlerEntries.Should().Be(0);
    }

    [Fact]
    public async Task should_mark_a_pre_entry_fault_the_retry_wrapper_could_not_clear_as_never_started()
    {
        // given — the wrapper replays once, and the fault outlasts it
        var runner = _CreateRunner(retries: 1, beginFaults: 2);

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        (await act.Should().ThrowAsync<InboxAttemptNotStartedException>()).WithInnerException<TimeoutException>();
        _calls.Count(call => string.Equals(call, "begin", StringComparison.Ordinal)).Should().Be(2);
        _handlerEntries.Should().Be(0);
    }

    [Fact]
    public async Task should_leave_a_cancelled_attempt_a_cancellation_rather_than_a_never_started_attempt()
    {
        // given — the attempt's token is cancelled while the connection opens
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var runner = _CreateRunner(beginFault: new OperationCanceledException(cancellation.Token));

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), cancellation.Token);

        // then — the executor recognizes a stopping dispatch only by the cancellation itself
        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
        _handlerEntries.Should().Be(0);
    }

    [Fact]
    public async Task should_mark_a_cancellation_raised_while_the_attempt_token_is_live_as_never_started()
    {
        // given — a provider timeout surfaces as a cancellation the attempt did not ask for
        var runner = _CreateRunner(beginFault: new OperationCanceledException("open timed out"));

        // when
        var act = () => runner.ExecuteAsync(_message, _Handler(), AbortToken);

        // then
        (
            await act.Should().ThrowAsync<InboxAttemptNotStartedException>()
        ).WithInnerException<OperationCanceledException>();
        _handlerEntries.Should().Be(0);
    }

    #region Helpers

    private TestRunner _CreateRunner(
        FakeAttempt? attempt = null,
        int retries = 0,
        int beginFaults = 0,
        Exception? beginFault = null
    )
    {
        return new TestRunner(this, attempt ?? new FakeAttempt(this), retries, beginFault is null ? beginFaults : 1)
        {
            BeginFault = beginFault,
        };
    }

    private Func<IUnitOfWork, CancellationToken, Task> _Handler(Exception? fault = null)
    {
        return (unitOfWork, _) =>
        {
            unitOfWork.Should().BeSameAs(_unitOfWork);
            _handlerEntries++;
            _calls.Add("handler");

            return fault is null ? Task.CompletedTask : Task.FromException(fault);
        };
    }

    private void _Probe(InboxCommitProbe result)
    {
        _storage
            .ProbeReceivedInboxCommitAsync(_message, Arg.Any<CancellationToken>())
            .Returns(_ => _Record("probe", result));
    }

    private ValueTask<T> _Record<T>(string call, T result)
    {
        _calls.Add(call);
        return ValueTask.FromResult(result);
    }

    private ValueTask _RecordVoid(string call)
    {
        _calls.Add(call);
        return ValueTask.CompletedTask;
    }

    private static MediumMessage _CreateMessage()
    {
        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString(),
                    [Headers.MessageName] = "test.messageName",
                },
                "{}"
            ),
            Content = "{}",
            Lane = MessageLane.Bus,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private sealed class FakeAttempt(InboxTransactionRunnerTests test) : InboxAttemptTransaction
    {
        public Exception? CommitFault { get; init; }

        public Exception? RollbackFault { get; init; }

        public override IUnitOfWork UnitOfWork => test._unitOfWork;

        public override DbTransaction Transaction => test._transaction;

        public override ValueTask FlushAsync(CancellationToken cancellationToken) => test._RecordVoid("flush");

        public override Task CommitAsync(CancellationToken cancellationToken)
        {
            test._calls.Add("commit");
            return CommitFault is null ? Task.CompletedTask : Task.FromException(CommitFault);
        }

        public override Task RollbackAsync(CancellationToken cancellationToken)
        {
            test._calls.Add("rollback");
            return RollbackFault is null ? Task.CompletedTask : Task.FromException(RollbackFault);
        }

        public override ValueTask DisposeAsync() => test._RecordVoid("dispose");
    }

    private sealed class TestRunner(InboxTransactionRunnerTests test, FakeAttempt attempt, int retries, int beginFaults)
        : InboxTransactionRunner(test._resolver, test._storage, NullLogger.Instance)
    {
        private int _beginFaultsLeft = beginFaults;

        public Exception? BeginFault { get; init; }

        protected override ValueTask<InboxAttemptTransaction> BeginAttemptAsync(CancellationToken cancellationToken)
        {
            test._calls.Add("begin");
            if (_beginFaultsLeft > 0)
            {
                _beginFaultsLeft--;
                throw BeginFault ?? new TimeoutException("transient begin fault");
            }

            return ValueTask.FromResult<InboxAttemptTransaction>(attempt);
        }

        // Replays the attempt on any exception, as a provider execution strategy does on a transient fault.
        protected override async Task<ExceptionDispatchInfo?> ExecuteWithPreEntryRetryAsync(
            Func<CancellationToken, Task<ExceptionDispatchInfo?>> run,
            CancellationToken cancellationToken
        )
        {
            for (var attemptNumber = 0; ; attemptNumber++)
            {
                try
                {
                    return await run(cancellationToken);
                }
                catch (Exception) when (attemptNumber < retries) { }
            }
        }
    }

    #endregion
}
#pragma warning restore CA2000
