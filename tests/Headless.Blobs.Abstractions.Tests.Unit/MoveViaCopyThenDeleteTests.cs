// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internals;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Pins every branch of <see cref="BlobStorageHelpers.MoveViaCopyThenDeleteAsync"/> — the single shared
/// copy-then-delete move flow used by the AWS, Azure, file-system, and SFTP providers. Each test wires pure
/// delegates with local counters (no mocking framework) and asserts exactly which delegates ran, the return
/// value or rethrown exception identity, and which token each delegate received.
/// </summary>
public sealed class MoveViaCopyThenDeleteTests : TestBase
{
    private static void _UnexpectedInvocation(string callbackName) =>
        false.Should().BeTrue("{0} must not be invoked", callbackName);

    [Fact]
    public async Task should_reject_occupied_destination_without_copying_or_deleting()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken? destinationCheckToken = null;
        var copyCalls = 0;
        var deleteCalls = 0;
        var sourceCheckCalls = 0;
        var rollbackCalls = 0;

        // Act
        var moved = await BlobStorageHelpers.MoveViaCopyThenDeleteAsync(
            destinationExistsAsync: ct =>
            {
                destinationCheckToken = ct;

                return ValueTask.FromResult(true);
            },
            copyAsync: _ =>
            {
                copyCalls++;

                return ValueTask.FromResult(true);
            },
            deleteSourceAsync: _ =>
            {
                deleteCalls++;

                return ValueTask.FromResult(true);
            },
            sourceExistsAsync: _ =>
            {
                sourceCheckCalls++;

                return ValueTask.FromResult(true);
            },
            rollbackDestinationAsync: _ =>
            {
                rollbackCalls++;

                return ValueTask.CompletedTask;
            },
            logDestinationKeptSourceGone: _ => _UnexpectedInvocation("logDestinationKeptSourceGone"),
            logSourceCheckFailed: _ => _UnexpectedInvocation("logSourceCheckFailed"),
            cancellationToken: cts.Token
        );

        // Assert
        moved.Should().BeFalse();
        destinationCheckToken.Should().Be(cts.Token);
        copyCalls.Should().Be(0);
        deleteCalls.Should().Be(0);
        sourceCheckCalls.Should().Be(0);
        rollbackCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_return_false_without_deleting_source_when_copy_fails()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken? copyToken = null;
        var deleteCalls = 0;
        var sourceCheckCalls = 0;
        var rollbackCalls = 0;

        // Act
        var moved = await BlobStorageHelpers.MoveViaCopyThenDeleteAsync(
            destinationExistsAsync: _ => ValueTask.FromResult(false),
            copyAsync: ct =>
            {
                copyToken = ct;

                return ValueTask.FromResult(false);
            },
            deleteSourceAsync: _ =>
            {
                deleteCalls++;

                return ValueTask.FromResult(true);
            },
            sourceExistsAsync: _ =>
            {
                sourceCheckCalls++;

                return ValueTask.FromResult(true);
            },
            rollbackDestinationAsync: _ =>
            {
                rollbackCalls++;

                return ValueTask.CompletedTask;
            },
            logDestinationKeptSourceGone: _ => _UnexpectedInvocation("logDestinationKeptSourceGone"),
            logSourceCheckFailed: _ => _UnexpectedInvocation("logSourceCheckFailed"),
            cancellationToken: cts.Token
        );

        // Assert
        moved.Should().BeFalse();
        copyToken.Should().Be(cts.Token);
        deleteCalls.Should().Be(0);
        sourceCheckCalls.Should().Be(0);
        rollbackCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_complete_move_when_source_delete_succeeds()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken? deleteToken = null;
        var sourceCheckCalls = 0;
        var rollbackCalls = 0;

        // Act
        var moved = await BlobStorageHelpers.MoveViaCopyThenDeleteAsync(
            destinationExistsAsync: _ => ValueTask.FromResult(false),
            copyAsync: _ => ValueTask.FromResult(true),
            deleteSourceAsync: ct =>
            {
                deleteToken = ct;

                return ValueTask.FromResult(true);
            },
            sourceExistsAsync: _ =>
            {
                sourceCheckCalls++;

                return ValueTask.FromResult(true);
            },
            rollbackDestinationAsync: _ =>
            {
                rollbackCalls++;

                return ValueTask.CompletedTask;
            },
            logDestinationKeptSourceGone: _ => _UnexpectedInvocation("logDestinationKeptSourceGone"),
            logSourceCheckFailed: _ => _UnexpectedInvocation("logSourceCheckFailed"),
            cancellationToken: cts.Token
        );

        // Assert
        moved.Should().BeTrue();
        deleteToken.Should().Be(cts.Token);
        sourceCheckCalls.Should().Be(0);
        rollbackCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_keep_destination_and_report_success_when_source_delete_returns_false()
    {
        // Arrange - a false source delete means a concurrent delete raced the move: the destination already holds
        // the data, so the move is complete. Rolling back here would destroy the only remaining copy.
        var sourceCheckCalls = 0;
        var rollbackCalls = 0;

        // Act
        var moved = await BlobStorageHelpers.MoveViaCopyThenDeleteAsync(
            destinationExistsAsync: _ => ValueTask.FromResult(false),
            copyAsync: _ => ValueTask.FromResult(true),
            deleteSourceAsync: _ => ValueTask.FromResult(false),
            sourceExistsAsync: _ =>
            {
                sourceCheckCalls++;

                return ValueTask.FromResult(false);
            },
            rollbackDestinationAsync: _ =>
            {
                rollbackCalls++;

                return ValueTask.CompletedTask;
            },
            logDestinationKeptSourceGone: _ => _UnexpectedInvocation("logDestinationKeptSourceGone"),
            logSourceCheckFailed: _ => _UnexpectedInvocation("logSourceCheckFailed"),
            cancellationToken: AbortToken
        );

        // Assert
        moved.Should().BeTrue();
        sourceCheckCalls.Should().Be(0);
        rollbackCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_roll_back_destination_and_rethrow_when_source_delete_throws_and_source_intact()
    {
        // Arrange - the re-check confirms the source is intact, so rolling back the copy cannot lose data and the
        // original delete exception must surface unchanged.
        using var cts = new CancellationTokenSource();
        var deleteException = new InvalidOperationException("delete failed");
        CancellationToken? sourceCheckToken = null;
        var rollbackCalls = 0;
        Exception? rollbackException = null;

        // Act
        var act = async () =>
            await BlobStorageHelpers.MoveViaCopyThenDeleteAsync(
                destinationExistsAsync: _ => ValueTask.FromResult(false),
                copyAsync: _ => ValueTask.FromResult(true),
                deleteSourceAsync: _ => ValueTask.FromException<bool>(deleteException),
                sourceExistsAsync: ct =>
                {
                    sourceCheckToken = ct;

                    return ValueTask.FromResult(true);
                },
                rollbackDestinationAsync: e =>
                {
                    rollbackCalls++;
                    rollbackException = e;

                    return ValueTask.CompletedTask;
                },
                logDestinationKeptSourceGone: _ => _UnexpectedInvocation("logDestinationKeptSourceGone"),
                logSourceCheckFailed: _ => _UnexpectedInvocation("logSourceCheckFailed"),
                cancellationToken: cts.Token
            );

        // Assert - the ORIGINAL delete exception is rethrown (same instance, not a wrapper).
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(deleteException);

        rollbackCalls.Should().Be(1);
        rollbackException.Should().BeSameAs(deleteException);

        // Compensation must still run when the move itself was cancelled: the re-check gets CancellationToken.None.
        sourceCheckToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task should_keep_destination_and_report_success_when_source_delete_throws_and_source_gone()
    {
        // Arrange - the delete faulted after the source blob was already removed: the destination is the sole
        // surviving copy, so it is kept and the move reports success instead of rethrowing.
        var deleteException = new InvalidOperationException("delete failed");
        CancellationToken? sourceCheckToken = null;
        var rollbackCalls = 0;
        var keptLogCalls = 0;
        Exception? keptLogException = null;

        // Act
        var moved = await BlobStorageHelpers.MoveViaCopyThenDeleteAsync(
            destinationExistsAsync: _ => ValueTask.FromResult(false),
            copyAsync: _ => ValueTask.FromResult(true),
            deleteSourceAsync: _ => ValueTask.FromException<bool>(deleteException),
            sourceExistsAsync: ct =>
            {
                sourceCheckToken = ct;

                return ValueTask.FromResult(false);
            },
            rollbackDestinationAsync: _ =>
            {
                rollbackCalls++;

                return ValueTask.CompletedTask;
            },
            logDestinationKeptSourceGone: e =>
            {
                keptLogCalls++;
                keptLogException = e;
            },
            logSourceCheckFailed: _ => _UnexpectedInvocation("logSourceCheckFailed"),
            cancellationToken: AbortToken
        );

        // Assert
        moved.Should().BeTrue();
        rollbackCalls.Should().Be(0);
        keptLogCalls.Should().Be(1);
        keptLogException.Should().BeSameAs(deleteException);
        sourceCheckToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task should_skip_rollback_and_rethrow_delete_exception_when_source_check_fails()
    {
        // Arrange - when the re-check itself fails, the source state is unknown: data-safety bias skips the
        // rollback (worst case two surviving copies, never zero) and the ORIGINAL delete exception propagates.
        var deleteException = new InvalidOperationException("delete failed");
        var checkException = new TimeoutException("source check failed");
        CancellationToken? sourceCheckToken = null;
        var rollbackCalls = 0;
        var checkFailedLogCalls = 0;
        Exception? checkFailedLogException = null;

        // Act
        var act = async () =>
            await BlobStorageHelpers.MoveViaCopyThenDeleteAsync(
                destinationExistsAsync: _ => ValueTask.FromResult(false),
                copyAsync: _ => ValueTask.FromResult(true),
                deleteSourceAsync: _ => ValueTask.FromException<bool>(deleteException),
                sourceExistsAsync: ct =>
                {
                    sourceCheckToken = ct;

                    return ValueTask.FromException<bool>(checkException);
                },
                rollbackDestinationAsync: _ =>
                {
                    rollbackCalls++;

                    return ValueTask.CompletedTask;
                },
                logDestinationKeptSourceGone: _ => _UnexpectedInvocation("logDestinationKeptSourceGone"),
                logSourceCheckFailed: e =>
                {
                    checkFailedLogCalls++;
                    checkFailedLogException = e;
                },
                cancellationToken: AbortToken
            );

        // Assert - the delete exception (not the check exception) is rethrown, as the same instance.
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(deleteException);

        rollbackCalls.Should().Be(0);
        checkFailedLogCalls.Should().Be(1);
        checkFailedLogException.Should().BeSameAs(checkException);
        sourceCheckToken.Should().Be(CancellationToken.None);
    }
}
