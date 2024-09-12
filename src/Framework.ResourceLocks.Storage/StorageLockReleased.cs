// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.ResourceLocks.Caching;

public sealed record StorageLockReleased(string Resource, string LockId);
