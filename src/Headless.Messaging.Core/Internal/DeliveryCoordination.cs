// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
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

    /// <summary>The relational resource's transaction has already committed or rolled back, so nothing can join it.</summary>
    TransactionCompleted = 4,
}

internal readonly record struct DeliveryCoordination
{
    private DeliveryCoordination(
        DeliveryCoordinationStatus status,
        DeliveryCoordinationMismatch mismatch,
        IUnitOfWork? unitOfWork,
        DbTransaction? transaction,
        MessagingOutbox? outbox = null
    )
    {
        Status = status;
        Mismatch = mismatch;
        UnitOfWork = unitOfWork;
        Transaction = transaction;
        Outbox = outbox;
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

    /// <summary>
    /// The additional outbox whose database the unit's transaction belongs to, or <see langword="null" /> when the row
    /// goes to the primary storage.
    /// </summary>
    internal MessagingOutbox? Outbox { get; }

    /// <summary>Routes a compatible coordination to the additional outbox that resolved it.</summary>
    internal DeliveryCoordination WithOutbox(MessagingOutbox outbox)
    {
        Argument.IsNotNull(outbox);

        if (Status is not DeliveryCoordinationStatus.Compatible)
        {
            throw new InvalidOperationException("Only a compatible coordination can be routed to an outbox storage.");
        }

        return new DeliveryCoordination(Status, Mismatch, UnitOfWork, Transaction, outbox);
    }

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
