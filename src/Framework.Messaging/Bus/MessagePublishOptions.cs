// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

[PublicAPI]
public sealed class PublishMessageOptions
{
    public required string UniqueId { get; set; }

    public required string CorrelationId { get; set; }

    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
