// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>The type of entity change that triggered the audit log entry.</summary>
public enum AuditChangeType
{
    /// <summary>Entity was inserted.</summary>
    Created,

    /// <summary>Entity was modified.</summary>
    Updated,

    /// <summary>Entity was hard-deleted.</summary>
    Deleted,
}
