// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Storage.InMemory;

internal sealed class MemoryMessage : MediumMessage
{
    public required string Name { get; init; }

    public string Group { get; init; } = null!;

    public StatusName StatusName { get; set; }

    public bool IsCurrentGeneration { get; set; } = true;

    public Guid? LifecycleId { get; set; }

    public Guid? ReplayParentIncarnationId { get; set; }

    public Guid? ReplayOperationId { get; set; }

    public DateTimeOffset? TerminalAt { get; set; }

    public DateTimeOffset? EffectiveExpiresAt { get; set; }

    public bool IsHeld { get; set; }

    public DateTimeOffset? HeldAt { get; set; }

    public string? HeldBy { get; set; }

    public string? HoldReason { get; set; }

    public Guid? HoldOperationId { get; set; }

    public TimeSpan InboxRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Version identifier copied from <c>MessagingOptions.Version</c> at write time.
    /// Pickup and scheduler queries filter on this to isolate messages across version boundaries,
    /// matching the SQL providers' <c>WHERE Version = @Version</c> behavior.
    /// </summary>
    public required string Version { get; init; }
}
