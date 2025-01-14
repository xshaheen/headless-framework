// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public interface IMessageSubscribeMedium<out TPayload>
{
    string UniqueId { get; }

    string Type { get; }

    string? CorrelationId { get; }

    IDictionary<string, string>? Properties { get; }

    TPayload Payload { get; }
}

public sealed class MessageSubscribeMedium<TPayload> : IMessageSubscribeMedium<TPayload>
{
    public required string UniqueId { get; init; }

    public required string Type { get; init; }

    public required string? CorrelationId { get; init; }

    public required IDictionary<string, string>? Properties { get; init; }

    public required TPayload Payload { get; init; }
}
