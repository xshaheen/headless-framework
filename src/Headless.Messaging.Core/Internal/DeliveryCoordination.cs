// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.UnitOfWork;

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
        IUnitOfWork? unitOfWork,
        DbTransaction? transaction
    )
    {
        Status = status;
        Mismatch = mismatch;
        UnitOfWork = unitOfWork;
        Transaction = transaction;
    }

    internal static DeliveryCoordination None => default;

    internal DeliveryCoordinationStatus Status { get; }

    internal DeliveryCoordinationMismatch Mismatch { get; }

    internal IUnitOfWork? UnitOfWork { get; }

    /// <summary>
    /// The live relational transaction the durable row must join, or <see langword="null" /> for a non-relational
    /// scope whose storage captures rows on the unit of work itself (see <see cref="ICoordinatedMessageStore" />).
    /// </summary>
    internal DbTransaction? Transaction { get; }

    internal static DeliveryCoordination Compatible(IUnitOfWork unitOfWork, DbTransaction? transaction)
    {
        return new DeliveryCoordination(
            DeliveryCoordinationStatus.Compatible,
            DeliveryCoordinationMismatch.None,
            Argument.IsNotNull(unitOfWork),
            transaction
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
            unitOfWork: null,
            transaction: null
        );
    }
}
