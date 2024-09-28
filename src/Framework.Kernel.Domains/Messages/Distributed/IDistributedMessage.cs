// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface IDistributedMessage
{
    string UniqueId { get; }

    DateTimeOffset Timestamp { get; }

    IDictionary<string, string> Properties { get; }
}

public interface IDistributedMessage<out T> : IDistributedMessage
{
    T Payload { get; }
}
