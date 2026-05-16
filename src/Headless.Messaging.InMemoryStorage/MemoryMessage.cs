// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Headless.Messaging.InMemoryStorage;

internal sealed class MemoryMessage : MediumMessage
{
    public required string Name { get; init; }

    public string Group { get; init; } = null!;

    public StatusName StatusName { get; set; }

    /// <summary>
    /// Version identifier copied from <c>MessagingOptions.Version</c> at write time.
    /// Pickup and scheduler queries filter on this to isolate messages across version boundaries,
    /// matching the SQL providers' <c>WHERE Version = @Version</c> behavior.
    /// </summary>
    public required string Version { get; init; }
}
