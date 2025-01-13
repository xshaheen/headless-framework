// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.ResourceLocks.Storage.RegularLocks;

public sealed record StorageResourceLockReleased(string Resource, string LockId);
