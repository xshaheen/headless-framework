// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.ResourceLocks.RegularLocks;

public sealed record ResourceLockReleased(string Resource, string LockId);
