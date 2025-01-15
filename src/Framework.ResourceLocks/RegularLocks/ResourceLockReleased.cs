// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.ResourceLocks.RegularLocks;

public sealed class ResourceLockReleased
{
    public required string Resource { get; init; }

    public required string LockId { get; init; }
}
