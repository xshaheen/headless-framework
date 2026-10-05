// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Headless.UnitOfWork.Analyzers;

/// <summary>
/// One autonomous receiver the analyzer watches, the members it flags, and the enlisted receiver on the unit of work
/// that replaces it. Receivers are matched by metadata name so the analyzer references none of their packages.
/// </summary>
internal sealed class ReceiverRule(
    DiagnosticDescriptor descriptor,
    string receiverMetadataName,
    ImmutableHashSet<string> members,
    string accessor,
    AccessorShape accessorShape,
    string? enlistedMetadataName
)
{
    public DiagnosticDescriptor Descriptor { get; } = descriptor;

    /// <summary>Metadata name of the autonomous receiver type, such as <c>Headless.Messaging.IBus</c>.</summary>
    public string ReceiverMetadataName { get; } = receiverMetadataName;

    /// <summary>Names of the flagged members, matched on interface members and on extension members of the receiver.</summary>
    public ImmutableHashSet<string> Members { get; } = members;

    /// <summary>Name of the unit-of-work extension member that returns the enlisted receiver, such as <c>Outbox</c>.</summary>
    public string Accessor { get; } = accessor;

    public AccessorShape AccessorShape { get; } = accessorShape;

    /// <summary>
    /// Metadata name of the type the accessor returns, used to check that a rewritten call binds. <see langword="null"/>
    /// when the enlisted call has a different shape and no fix is offered; the receiver's own type is used when the
    /// enlisted receiver is the same interface.
    /// </summary>
    public string? EnlistedMetadataName { get; } = enlistedMetadataName;

    /// <summary>Whether the enlisted receiver is the autonomous interface itself, as it is for the Jobs accessors.</summary>
    public bool EnlistedIsReceiver =>
        string.Equals(EnlistedMetadataName, ReceiverMetadataName, StringComparison.Ordinal);

    public static ImmutableArray<ReceiverRule> All { get; } =
    [
        new(
            DiagnosticDescriptors.OutboxReceiver,
            "Headless.Messaging.IBus",
            ["PublishAsync"],
            "Outbox",
            AccessorShape.Property,
            "Headless.Messaging.UnitOfWorkOutbox"
        ),
        new(
            DiagnosticDescriptors.OutboxReceiver,
            "Headless.Messaging.IQueue",
            ["EnqueueAsync"],
            "Outbox",
            AccessorShape.Property,
            "Headless.Messaging.UnitOfWorkOutbox"
        ),
        // Only the Jobs members that pass the unit through enlist; cancel, pause, resume, requeue, update, and delete
        // ignore it even through unit.Jobs, so reporting them would promise atomicity the enlisted call does not give.
        new(
            DiagnosticDescriptors.JobsReceiver,
            "Headless.Jobs.IJobScheduler",
            [
                "EnqueueAsync",
                "ScheduleAsync",
                "ScheduleAfterAsync",
                "ScheduleRecurringAsync",
                "ScheduleKeyedAsync",
                "ReplaceKeyedAsync",
                "CancelKeyedAsync",
            ],
            "Jobs",
            AccessorShape.Property,
            "Headless.Jobs.IJobScheduler"
        ),
        new(
            DiagnosticDescriptors.JobsReceiver,
            "Headless.Jobs.ITimeJobManager`1",
            ["AddAsync", "AddIdempotentAsync", "AddBatchAsync", "ScheduleKeyedAsync", "CancelKeyedAsync"],
            "TimeJobs",
            AccessorShape.GenericMethod,
            "Headless.Jobs.ITimeJobManager`1"
        ),
        new(
            DiagnosticDescriptors.JobsReceiver,
            "Headless.Jobs.ICronJobManager`1",
            ["AddAsync", "AddBatchAsync"],
            "CronJobs",
            AccessorShape.GenericMethod,
            "Headless.Jobs.ICronJobManager`1"
        ),
        // The transaction lock takes an acquire timeout and returns a different handle, so no fix is offered.
        new(
            DiagnosticDescriptors.TransactionLocksReceiver,
            "Headless.DistributedLocks.IDistributedLock",
            ["AcquireAsync", "TryAcquireAsync"],
            "TransactionLocks",
            AccessorShape.Property,
            enlistedMetadataName: null
        ),
        new(
            DiagnosticDescriptors.LeasesReceiver,
            "Headless.Fencing.IFencedLeases",
            ["GrantAsync", "RenewAsync", "SettleAsync", "ReleaseAsync"],
            "Leases",
            AccessorShape.Property,
            "Headless.Fencing.UnitOfWorkLeases"
        ),
        new(
            DiagnosticDescriptors.IdempotencyReceiver,
            "Headless.Idempotency.IIdempotentOperations",
            ["AdmitAsync", "CompleteAsync", "SetRecoveryPointAsync", "ReleaseAsync"],
            "Idempotency",
            AccessorShape.Property,
            "Headless.Idempotency.UnitOfWorkIdempotency"
        ),
    ];
}
