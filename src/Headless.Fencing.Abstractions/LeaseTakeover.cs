// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>What a grant does when the lease's last attempt expired without ending.</summary>
[PublicAPI]
public enum LeaseTakeover
{
    /// <summary>
    /// Take the expired lease over at once with a new generation (<see cref="LeaseGrantStatus.Takeover" />). Use it
    /// for work that is safe to repeat or that resumes from its recorded progress.
    /// </summary>
    Allowed = 0,

    /// <summary>
    /// Refuse while the expired attempt is still active (<see cref="LeaseGrantStatus.Expired" />), and grant only after
    /// a sweep abandoned it or it was settled or released. Use it for work that may already have produced an external
    /// effect, so a person or a handler decides what happens next instead of the next grant.
    /// </summary>
    AfterSweep = 1,
}
