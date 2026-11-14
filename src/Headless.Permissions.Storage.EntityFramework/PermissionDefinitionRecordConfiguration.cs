// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Permissions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Permissions;

/// <summary>EF Core entity type configuration for <see cref="PermissionDefinitionRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name, schema, and column lengths.</param>
/// <param name="style">The naming style of the database the model targets.</param>
internal sealed class PermissionDefinitionRecordConfiguration(
    PermissionsStorageOptions options,
    StorageNamingStyle style
) : IEntityTypeConfiguration<PermissionDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<PermissionDefinitionRecord> b)
    {
        var table = options.ResolvePermissionDefinitionsTableName(style);

        b.ToTable(table, options.Schema);
        b.TryConfigureExtraProperties();
        b.HasKey(x => x.Id).HasName(HeadlessStorageNaming.PrimaryKeyName(style, table));
        b.Property(x => x.GroupName).HasMaxLength(options.PermissionGroupDefinitionsNameMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(options.PermissionDefinitionsNameMaxLength).IsRequired();
        b.Property(x => x.ParentName).HasMaxLength(options.PermissionDefinitionsNameMaxLength);
        b.Property(x => x.DisplayName).HasMaxLength(options.PermissionDefinitionsDisplayNameMaxLength).IsRequired();
        b.Property(x => x.Providers).HasMaxLength(options.PermissionDefinitionsProvidersMaxLength);
        b.HasIndex(x => new { x.Name })
            .IsUnique()
            .HasDatabaseName(HeadlessStorageNaming.IndexName(style, table, PermissionsStorageNames.DefinitionsByName));
        b.HasIndex(x => new { x.GroupName })
            .HasDatabaseName(
                HeadlessStorageNaming.IndexName(style, table, PermissionsStorageNames.DefinitionsByGroupName)
            );
        b.ApplyColumnNaming(style);
    }
}
