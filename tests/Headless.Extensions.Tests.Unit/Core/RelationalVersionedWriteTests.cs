// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless;
using Headless.Testing.Tests;

namespace Tests.Core;

public sealed class RelationalVersionedWriteTests : TestBase
{
    private const string _Expected = "v1";
    private const string _Message = "The group changed.";

    [Fact]
    public async Task should_return_the_version_an_attempt_commits()
    {
        var reads = 0;

        var version = await _ExecuteAsync(
            (attempt, _) =>
            {
                attempt.BeginCommit("v2");
                return Task.FromResult("v2");
            },
            _ =>
            {
                reads++;
                return Task.FromResult(_Expected);
            }
        );

        version.Should().Be("v2");
        reads.Should().Be(0);
    }

    [Fact]
    public async Task should_retry_a_conflict_that_left_the_version_unchanged()
    {
        var attempts = 0;

        var version = await _ExecuteAsync(
            (attempt, _) =>
            {
                if (++attempts == 1)
                {
                    throw _SerializationFailure();
                }

                attempt.BeginCommit("v2");
                return Task.FromResult("v2");
            },
            _ => Task.FromResult(_Expected)
        );

        version.Should().Be("v2");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_reject_a_conflict_after_which_the_group_changed()
    {
        var attempts = 0;
        var conflict = _SerializationFailure();

        var act = () =>
            _ExecuteAsync(
                (_, _) =>
                {
                    attempts++;
                    throw conflict;
                },
                _ => Task.FromResult("someone-else")
            );

        var exception = (await act.Should().ThrowAsync<DBConcurrencyException>().WithMessage(_Message)).Which;
        exception.InnerException.Should().BeSameAs(conflict);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_reject_conflicts_that_outlast_every_attempt_instead_of_rethrowing_the_driver_error()
    {
        var attempts = 0;

        var act = () =>
            _ExecuteAsync(
                (_, _) =>
                {
                    attempts++;
                    throw _SerializationFailure();
                },
                _ => Task.FromResult(_Expected)
            );

        await act.Should().ThrowAsync<DBConcurrencyException>().WithMessage(_Message);
        attempts.Should().Be(RelationalVersionedWrite.MaxAttempts);
    }

    [Fact]
    public async Task should_treat_a_conflict_reported_by_the_commit_as_rolled_back()
    {
        var attempts = 0;

        var version = await _ExecuteAsync(
            (attempt, _) =>
            {
                attempt.BeginCommit("v2");

                if (++attempts == 1)
                {
                    throw _SerializationFailure();
                }

                return Task.FromResult("v2");
            },
            _ => Task.FromResult(_Expected)
        );

        version.Should().Be("v2");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_confirm_a_lost_commit_reply_when_storage_holds_the_written_version()
    {
        var attempts = 0;

        var version = await _ExecuteAsync(
            (attempt, _) =>
            {
                attempts++;
                attempt.BeginCommit("v2");
                throw new IOException("connection reset");
            },
            _ => Task.FromResult("v2")
        );

        version.Should().Be("v2");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_rethrow_a_lost_commit_reply_when_storage_does_not_hold_the_written_version()
    {
        var attempts = 0;
        var lost = new IOException("connection reset");

        var act = () =>
            _ExecuteAsync(
                (attempt, _) =>
                {
                    attempts++;
                    attempt.BeginCommit("v2");
                    throw lost;
                },
                _ => Task.FromResult(_Expected)
            );

        (await act.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(lost);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_rethrow_a_failure_before_the_commit_without_reading_again()
    {
        var reads = 0;
        var mismatch = new DBConcurrencyException("stale");

        var act = () =>
            _ExecuteAsync(
                (_, _) => throw mismatch,
                _ =>
                {
                    reads++;
                    return Task.FromResult(_Expected);
                }
            );

        (await act.Should().ThrowAsync<DBConcurrencyException>()).Which.Should().BeSameAs(mismatch);
        reads.Should().Be(0);
    }

    [Fact]
    public async Task should_rethrow_the_write_failure_when_reading_again_fails()
    {
        var conflict = _SerializationFailure();

        var act = () => _ExecuteAsync((_, _) => throw conflict, _ => throw new IOException("database unreachable"));

        (await act.Should().ThrowAsync<DbException>()).Which.Should().BeSameAs(conflict);
    }

    [Fact]
    public async Task should_not_settle_a_failure_once_the_caller_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var reads = 0;
        var lost = new IOException("connection reset");

        var act = () =>
            RelationalVersionedWrite.ExecuteAsync(
                (attempt, _) =>
                {
                    attempt.BeginCommit("v2");
                    cancellation.Cancel();
                    throw lost;
                },
                _ =>
                {
                    reads++;
                    return Task.FromResult("v2");
                },
                _Expected,
                _Message,
                cancellation.Token
            );

        (await act.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(lost);
        reads.Should().Be(0);
    }

    private static Task<string> _ExecuteAsync(
        Func<RelationalVersionedWriteAttempt, CancellationToken, Task<string>> attempt,
        Func<CancellationToken, Task<string>> readVersion
    )
    {
        return RelationalVersionedWrite.ExecuteAsync(attempt, readVersion, _Expected, _Message, AbortToken);
    }

    private static FakeDbException _SerializationFailure() => new("40001");

    private sealed class FakeDbException(string sqlState) : DbException("fake database failure")
    {
        public override string? SqlState { get; } = sqlState;
    }
}
