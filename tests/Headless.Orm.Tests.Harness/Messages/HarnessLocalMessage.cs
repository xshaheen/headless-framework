// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;

namespace Tests.Messages;

/// <summary>
/// Test local message for verifying message publishing behavior.
/// </summary>
public sealed record HarnessLocalMessage(string Text) : ILocalMessage;
