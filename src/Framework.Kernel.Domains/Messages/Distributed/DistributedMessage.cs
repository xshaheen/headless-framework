// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

[PublicAPI]
public class DistributedMessage : EqualityBase<DistributedMessage>, IDistributedMessage
{
    public required string UniqueId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return UniqueId;
    }
}

[PublicAPI]
public class DistributedMessage<T> : DistributedMessage, IDistributedMessage<T>
{
    public required T Payload { get; init; }
}
