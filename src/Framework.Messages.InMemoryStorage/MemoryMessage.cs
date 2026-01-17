// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;

namespace Framework.Messages;

internal class MemoryMessage : MediumMessage
{
    public string Name { get; set; } = default!;

    public StatusName StatusName { get; set; }

    public string Group { get; set; } = default!;
}
