// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.CommitCoordination;

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

        return new DeliveryCoordination(DeliveryCoordinationStatus.Incompatible, mismatch, null, null);
    }
}
