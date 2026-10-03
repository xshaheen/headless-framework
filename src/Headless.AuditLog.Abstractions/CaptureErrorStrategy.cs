// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>Strategy applied when audit capture throws.</summary>
public enum CaptureErrorStrategy
{
    /// <summary>
    /// Log the failure as an error and continue the entity save. A per-entity capture failure
    /// skips only that entity's audit entry; a whole-capture failure skips all audit entries
    /// for the batch.
    /// </summary>
    Continue = 0,

    /// <summary>Log the failure and rethrow so the entity save is aborted.</summary>
    Throw = 1,
}
