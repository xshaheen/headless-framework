// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Runtime;

/// <summary>The exact route an inbox row names; string parts compare ordinally, and a missing part matches only another.</summary>
internal readonly record struct InboxExecutorKey(
    string? ConsumerIdentity,
    string? ContractVersion,
    string MessageName,
    MessageLane Lane
);
