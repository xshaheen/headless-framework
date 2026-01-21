// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Headless.Messaging.InMemoryStorage;

internal sealed class MemoryMessage : MediumMessage
{
    public required string Name { get; init; }

    public string Group { get; init; } = null!;

    public StatusName StatusName { get; set; }
}
