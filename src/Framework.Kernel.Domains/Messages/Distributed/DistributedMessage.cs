// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

[PublicAPI]
public class DistributedMessage<T> : EqualityBase<DistributedMessage<T>>, IDistributedMessage<T>
{
    public required string UniqueId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public required T Payload { get; init; }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return UniqueId;
    }
}
