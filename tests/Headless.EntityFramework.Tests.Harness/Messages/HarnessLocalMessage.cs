// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Messages;

/// <summary>
/// Test local message for verifying message publishing behavior.
/// </summary>
public sealed record HarnessLocalMessage(string Text)
{
    public string UniqueId { get; } = Guid.NewGuid().ToString();
}
