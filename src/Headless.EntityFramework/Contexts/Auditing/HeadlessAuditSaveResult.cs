// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Headless.EntityFramework;

internal readonly record struct HeadlessAuditSaveResult(
    bool RequiresManualAcceptAllChanges,
    IReadOnlyList<IAuditLogStoreEntry>? AuditEntries
);
