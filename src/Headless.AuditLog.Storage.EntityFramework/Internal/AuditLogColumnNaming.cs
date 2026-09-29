// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.AuditLog.Internal;

/// <summary>Column naming for the audit-log entity configuration.</summary>
internal static class AuditLogColumnNaming
{
    extension(EntityTypeBuilder builder)
    {
        /// <summary>
        /// Names every mapped column after its property in <paramref name="style"/>, so the EF mapping produces the
        /// same columns as the raw provider for that database.
        /// </summary>
        /// <remarks>Call last: it walks the properties already on the entity and overrides their names.</remarks>
        public void ApplyColumnNaming(StorageNamingStyle style)
        {
            foreach (var property in builder.Metadata.GetProperties().ToList())
            {
                builder.Property(property.Name).HasColumnName(HeadlessStorageNaming.Apply(style, property.Name));
            }
        }
    }
}
