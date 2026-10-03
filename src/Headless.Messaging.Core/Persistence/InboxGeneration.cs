// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>The immutable identity allocated when an inbox generation is first admitted.</summary>
[PublicAPI]
public sealed record InboxGeneration(long Number, Guid IncarnationId);
