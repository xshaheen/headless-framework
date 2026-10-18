// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework;

/// <summary>Writes a framework stamp (tenant, concurrency stamp, create audit) on a tracked entity.</summary>
internal static class HeadlessEntryStamping
{
    /// <summary>
    /// Writes through the entry when the model maps the member, so EF Core flags it modified at once: audit capture
    /// and the base save see the stamp without another change-detection pass, and a concurrency token keeps its
    /// original value for the UPDATE's check. A member the model does not map is set through its CLR setter.
    /// </summary>
    public static void SetValue(EntityEntry entry, string propertyName, object? value)
    {
        if (entry.Metadata.FindProperty(propertyName) is { } property)
        {
            // The public entry, not InternalEntityEntry's indexer: that one saves a PropertyEntry per stamp, but EF Core
            // may change it in any release, and EF Core is not versioned in lockstep with this package.
            entry.Property(property).CurrentValue = value;

            return;
        }

        ObjectPropertiesHelper.TrySetPropertyValue(entry.Entity, propertyName, value);
    }
}
