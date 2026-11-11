// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.AuditLog.Internal;

/// <summary>Runs the registered enrichers over one audit row, shared by every path that adds a row to a context.</summary>
internal static class AuditLogEntryEnrichers
{
    public static void Enrich(this IEnumerable<IAuditLogEntryEnricher> enrichers, EntityEntry<AuditLogEntry> entry)
    {
        foreach (var enricher in enrichers)
        {
            enricher.Enrich(entry);
        }
    }
}
