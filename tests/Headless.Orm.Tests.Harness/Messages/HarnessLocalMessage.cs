// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Messages;

/// <summary>
/// Test local message for verifying message publishing behavior.
/// </summary>
public sealed record HarnessLocalMessage(string Text) : ILocalMessage
{
    public string UniqueId { get; } = Guid.NewGuid().ToString();
}
