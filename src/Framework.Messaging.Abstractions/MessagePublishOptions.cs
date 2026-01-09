// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

[PublicAPI]
public sealed class PublishMessageOptions
{
    public required Guid UniqueId { get; set; }

    public required Guid? CorrelationId { get; set; }

    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
