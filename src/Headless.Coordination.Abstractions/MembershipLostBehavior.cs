// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Behavior applied when the local process loses its own membership identity.</summary>
[PublicAPI]
public enum MembershipLostBehavior
{
    StopApplication = 0,
    StopMembershipOnly = 1,
}
