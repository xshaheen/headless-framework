// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.ResourceLocks.Storage.RegularLocks;

public sealed class StorageResourceLockReleased
{
    public required string Resource { get; init; }

    public required string LockId { get; init; }
}
