// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>The type of entity change that triggered the audit log entry.</summary>
public enum AuditChangeType
{
    /// <summary>Entity was inserted.</summary>
    Created = 0,

    /// <summary>Entity was modified.</summary>
    Updated = 1,

    /// <summary>Entity was hard-deleted.</summary>
    Deleted = 2,
}
