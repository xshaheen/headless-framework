// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Permissions;

internal sealed class PermissionGrantRecordConfiguration(PermissionsStorageOptions options)
    : IEntityTypeConfiguration<PermissionGrantRecord>
{
    public void Configure(EntityTypeBuilder<PermissionGrantRecord> b)
    {
        b.ToTable(options.PermissionGrantsTableName, options.Schema);
        b.ConfigureHeadlessConvention();
        b.Property(x => x.Name).HasMaxLength(PermissionGrantRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.ProviderName).HasMaxLength(PermissionGrantRecordConstants.ProviderNameMaxLength).IsRequired();
        b.Property(x => x.ProviderKey).HasMaxLength(PermissionGrantRecordConstants.ProviderKeyMaxLength).IsRequired();
        b.Property(x => x.TenantId).HasMaxLength(PermissionGrantRecordConstants.TenantIdMaxLength).IsRequired(false);

        // PostgreSQL and SQLite treat NULLs as distinct in a unique index, so a single index over the
        // nullable TenantId would let concurrent inserts create duplicate host (NULL-tenant) grant rows.
        // Mirror the raw PostgreSql/SqlServer initializers: one index per tenant nullability, same names.
        b.HasIndex(x => new
            {
                x.TenantId,
                x.Name,
                x.ProviderName,
                x.ProviderKey,
            })
            .IsUnique()
            .HasFilter("\"TenantId\" IS NOT NULL")
            .HasDatabaseName($"IX_{options.PermissionGrantsTableName}_TenantId_Name_ProviderName_ProviderKey");

        b.HasIndex(x => new
            {
                x.Name,
                x.ProviderName,
                x.ProviderKey,
            })
            .IsUnique()
            .HasFilter("\"TenantId\" IS NULL")
            .HasDatabaseName($"IX_{options.PermissionGrantsTableName}_Name_ProviderName_ProviderKey_NullTenantId");
    }
}
