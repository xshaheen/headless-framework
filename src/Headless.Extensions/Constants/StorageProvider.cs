// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// The relational providers whose identifier rules <see cref="StorageIdentifier" /> publishes. Provider packages
/// name their dialect through this value when they validate feature-owned schema and table names, so the rule set
/// lives in one place and every feature validates the same way.
/// </summary>
[PublicAPI]
public enum StorageProvider
{
    /// <summary>PostgreSQL (unquoted identifiers, 63-character cap).</summary>
    PostgreSql,

    /// <summary>SQL Server (regular identifiers, 128-character cap).</summary>
    SqlServer,
}
