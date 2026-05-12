// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Messages;

/// <summary>
/// Test distributed message for verifying message enqueue behavior.
/// </summary>
public sealed record HarnessDistributedMessage(string Text) : IDistributedMessage
{
    public string UniqueId { get; } = Guid.NewGuid().ToString();
}
