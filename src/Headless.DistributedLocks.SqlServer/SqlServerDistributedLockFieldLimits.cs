// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// Field length limits imposed by SQL Server for identifiers and application-lock resource names.
/// Used by the distributed-lock provider to validate and truncate values before issuing
/// <c>sp_getapplock</c> / <c>sp_releaseapplock</c> calls.
/// </summary>
[PublicAPI]
public static class SqlServerDistributedLockFieldLimits
{
    /// <summary>
    /// Maximum number of characters allowed for the <c>@Resource</c> parameter of
    /// <c>sys.sp_getapplock</c> and <c>sys.sp_releaseapplock</c>. Resource names longer than this limit
    /// are automatically hashed by <see cref="SqlServerResourceName.Encode"/> to fit within the budget.
    /// </summary>
    public const int MaxResourceNameLength = 255;

    /// <summary>
    /// Maximum number of characters allowed for a SQL Server identifier (schema name, sequence name, etc.)
    /// as defined by <c>sysname</c> / T-SQL identifier rules. Sequence names derived from the configured
    /// key prefix are truncated to this length by <see cref="SqlServerIdentifier.FenceSequenceName"/>.
    /// </summary>
    public const int MaxIdentifierLength = 128;
}
