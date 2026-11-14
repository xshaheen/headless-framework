// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Permissions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Permissions;

/// <summary>EF Core entity type configuration for <see cref="PermissionGroupDefinitionRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name, schema, and column lengths.</param>
/// <param name="style">The naming style of the database the model targets.</param>
internal sealed class PermissionGroupDefinitionRecordConfiguration(
    PermissionsStorageOptions options,
    StorageNamingStyle style
) : IEntityTypeConfiguration<PermissionGroupDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<PermissionGroupDefinitionRecord> b)
    {
        var table = options.ResolvePermissionGroupDefinitionsTableName(style);

        b.ToTable(table, options.Schema);
        b.TryConfigureExtraProperties();
        b.HasKey(x => x.Id).HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));
        b.Property(x => x.Name).HasMaxLength(options.PermissionGroupDefinitionsNameMaxLength).IsRequired();
        b.Property(x => x.DisplayName)
            .HasMaxLength(options.PermissionGroupDefinitionsDisplayNameMaxLength)
            .IsRequired();
        b.HasIndex(x => new { x.Name })
            .IsUnique()
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, PermissionsStorageNames.GroupsByName));
        b.ApplyColumnNaming(style);
    }
}
