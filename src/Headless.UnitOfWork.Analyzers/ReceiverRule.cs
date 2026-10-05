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
    ];
}
