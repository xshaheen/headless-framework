// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Domains;

public interface IDistributedMessage
{
    Guid UniqueId { get; }

    DateTimeOffset Timestamp { get; }

    IDictionary<string, string> Properties { get; }
}

public interface IDistributedMessage<out T> : IDistributedMessage
{
    T Payload { get; }
}
