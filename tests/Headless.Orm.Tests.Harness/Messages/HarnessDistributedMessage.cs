// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Messages;

/// <summary>
/// Test distributed message for verifying message publishing behavior.
/// </summary>
public sealed record HarnessDistributedMessage(string Text) : IDistributedMessage
{
    public Guid UniqueId { get; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
