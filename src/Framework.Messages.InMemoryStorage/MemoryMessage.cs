// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;

namespace Framework.Messages;

internal sealed class MemoryMessage : MediumMessage
{
    public required string Name { get; init; }

    public string Group { get; init; } = null!;

    public StatusName StatusName { get; set; }
}
