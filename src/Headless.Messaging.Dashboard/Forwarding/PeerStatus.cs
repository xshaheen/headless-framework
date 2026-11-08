// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>What the last forwarded call to a peer found.</summary>
internal sealed record PeerStatus(PeerState State, DateTimeOffset At);
