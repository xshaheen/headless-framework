// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

internal readonly record struct HeadlessAuditSaveResult(
    bool RequiresManualAcceptAllChanges,
    IReadOnlyList<IAuditLogStoreEntry>? AuditEntries
);
