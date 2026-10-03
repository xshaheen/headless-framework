// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Payload-free filters for retained inbox generations.</summary>
[PublicAPI]
public sealed class InboxGenerationQuery
{
    public Guid? IncarnationId { get; set; }

    public string? ConsumerIdentity { get; set; }

    public MessageLane? Lane { get; set; }

    public StatusName? Status { get; set; }

    public bool? IsOrphaned { get; set; }

    public bool? IsHeld { get; set; }

    public int CurrentPage { get; set; }

    public int PageSize { get; set; } = 20;
}
