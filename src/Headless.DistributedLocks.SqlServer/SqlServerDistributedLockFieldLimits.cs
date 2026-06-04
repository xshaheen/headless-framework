// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks.SqlServer;

/// <summary>SQL Server field and identifier limits used by the distributed-lock provider.</summary>
[PublicAPI]
public static class SqlServerDistributedLockFieldLimits
{
    public const int MaxResourceNameLength = 255;
    public const int MaxIdentifierLength = 128;
}
