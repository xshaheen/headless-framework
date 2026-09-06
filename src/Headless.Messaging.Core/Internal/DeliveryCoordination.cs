// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Internal;

internal enum DeliveryCoordinationStatus
{
    None = 0,
    Compatible = 1,
    Incompatible = 2,
}

internal enum DeliveryCoordinationMismatch
{
    None = 0,
    MissingRelationalCapability = 1,
    StorageProvider = 2,
    Database = 3,
    InactiveTransaction = 4,
}

internal enum InboxCommitProbe
{
    Indeterminate = 0,
    Committed = 1,
}

internal interface ITransactionalInboxStorage
{
    ValueTask<bool> CompleteReceivedInboxAsync(
        MediumMessage message,
        DbTransaction transaction,
        CancellationToken cancellationToken
    );

    ValueTask<InboxCommitProbe> ProbeReceivedInboxCommitAsync(
        MediumMessage message,
        CancellationToken cancellationToken
    );
}

internal interface IInboxTransactionRunner
{
    Task ExecuteAsync(
        MediumMessage message,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken
    );
}

internal sealed class StaleInboxAttemptException(Guid storageId)
    : InvalidOperationException($"Inbox attempt '{storageId}' lost its generation fence before completion.");

internal sealed class UncommittedInboxCommitException(Guid storageId, Exception commitException)
    : InvalidOperationException(
        $"The coordinated commit for inbox attempt '{storageId}' was rolled back; persisted lease recovery must reserve the next attempt.",
        commitException
    );

internal sealed class IndeterminateInboxCommitException(
    Guid storageId,
    Exception commitException,
    Exception probeException
)
    : InvalidOperationException(
        $"The coordinated commit outcome for inbox attempt '{storageId}' is indeterminate; persisted recovery must resolve it before handler re-entry.",
        new AggregateException(commitException, probeException)
    );

internal readonly record struct DeliveryCoordination
{
    private DeliveryCoordination(
        DeliveryCoordinationStatus status,
        DeliveryCoordinationMismatch mismatch,
        ICommitCoordinator? coordinator,
        DbTransaction? transaction
    )
    {
        Status = status;
        Mismatch = mismatch;
        Coordinator = coordinator;
        Transaction = transaction;
    }

    internal static DeliveryCoordination None => default;

    internal DeliveryCoordinationStatus Status { get; }

    internal DeliveryCoordinationMismatch Mismatch { get; }

    internal ICommitCoordinator? Coordinator { get; }

    internal DbTransaction? Transaction { get; }

    internal static DeliveryCoordination Compatible(ICommitCoordinator coordinator, DbTransaction transaction)
    {
        return new DeliveryCoordination(
            DeliveryCoordinationStatus.Compatible,
            DeliveryCoordinationMismatch.None,
            Argument.IsNotNull(coordinator),
            Argument.IsNotNull(transaction)
        );
    }

    internal static DeliveryCoordination Incompatible(DeliveryCoordinationMismatch mismatch)
    {
        if (mismatch is DeliveryCoordinationMismatch.None || !Enum.IsDefined(mismatch))
        {
            throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, "A defined mismatch reason is required.");
        }

        return new DeliveryCoordination(
            DeliveryCoordinationStatus.Incompatible,
            mismatch,
            coordinator: null,
            transaction: null
        );
    }
}
