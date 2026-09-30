// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions.Entities;
using Headless.Permissions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Permissions;

/// <summary>EF Core entity type configuration for <see cref="PermissionGrantRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name and schema.</param>
/// <param name="style">The naming style of the database the model targets.</param>
internal sealed class PermissionGrantRecordConfiguration(PermissionsStorageOptions options, StorageNamingStyle style)
    : IEntityTypeConfiguration<PermissionGrantRecord>
{
    public void Configure(EntityTypeBuilder<PermissionGrantRecord> b)
    {
        var table = options.ResolvePermissionGrantsTableName(style);

        b.ToTable(table, options.Schema);
        b.ConfigureHeadlessConvention();
        b.HasKey(x => x.Id).HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));
        b.Property(x => x.Name).HasMaxLength(PermissionGrantRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.ProviderName).HasMaxLength(PermissionGrantRecordConstants.ProviderNameMaxLength).IsRequired();
        b.Property(x => x.ProviderKey).HasMaxLength(PermissionGrantRecordConstants.ProviderKeyMaxLength).IsRequired();
        b.Property(x => x.TenantId).HasMaxLength(PermissionGrantRecordConstants.TenantIdMaxLength).IsRequired(false);

        var tenantId = HeadlessStorageNaming.Apply(style, nameof(PermissionGrantRecord.TenantId));

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
            .HasFilter($"\"{tenantId}\" IS NOT NULL")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, PermissionsStorageNames.GrantsByTenant));

        b.HasIndex(x => new
            {
                x.Name,
                x.ProviderName,
                x.ProviderKey,
            })
            .IsUnique()
            .HasFilter($"\"{tenantId}\" IS NULL")
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, PermissionsStorageNames.GrantsByNoTenant));

        b.ApplyColumnNaming(style);
    }
}
