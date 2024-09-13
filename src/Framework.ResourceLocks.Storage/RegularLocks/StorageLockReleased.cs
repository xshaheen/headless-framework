// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.ResourceLocks.Storage.RegularLocks;

public sealed record StorageLockReleased(string Resource, string LockId);
