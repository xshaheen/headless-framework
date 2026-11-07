// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Storage.InMemory;

/// <summary>
/// The in-memory form of a <see cref="MessageListQuery"/>'s filters, shared by the list, the capped count, and an
/// Operation's scope read. Text compares ordinally, as the relational stores compare it byte for byte.
/// </summary>
internal static class InMemoryMessageFilter
{
    public static bool IsRecognizedLane(MessageLane lane) => lane is MessageLane.Bus or MessageLane.Queue;

    public static bool IsTerminal(MemoryMessage message) =>
        message.StatusName is StatusName.Succeeded or StatusName.Failed && message.NextRetryAt is null;

    public static bool Matches(MemoryMessage message, MessageListQuery query)
    {
        var received = query.MessageType == MessageType.Subscribe;

        return IsRecognizedLane(message.Lane)
            && (query.Status is not { } status || message.StatusName == status)
            && (!query.TerminalOnly || IsTerminal(message))
            && (query.Lane is not { } lane || message.Lane == lane)
            && (query.Name is null || string.Equals(message.Name, query.Name, StringComparison.Ordinal))
            && (query.MessageId is null || string.Equals(message.Origin.Id, query.MessageId, StringComparison.Ordinal))
            && (
                query.FailureFingerprint is null
                || string.Equals(message.FailureFingerprint, query.FailureFingerprint, StringComparison.Ordinal)
            )
            && (
                !received
                || query.ConsumerIdentity is null
                || string.Equals(message.ConsumerIdentity, query.ConsumerIdentity, StringComparison.Ordinal)
            )
            && (
                !received
                || query.TenantId is null
                || string.Equals(message.InboxKey?.TenantId, query.TenantId, StringComparison.Ordinal)
            )
            && (query.IsHeld is not { } held || (received ? message.IsHeld == held : !held));
    }
}
