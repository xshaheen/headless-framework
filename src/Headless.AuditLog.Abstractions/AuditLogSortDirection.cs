// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.AuditLog;

/// <summary>The order in which <see cref="IReadAuditLog{TContext}"/> returns entries.</summary>
public enum AuditLogSortDirection
{
    /// <summary>Most recent entries first: descending creation time, then descending ID.</summary>
    NewestFirst = 0,

    /// <summary>Oldest entries first: ascending creation time, then ascending ID.</summary>
    OldestFirst = 1,
}
