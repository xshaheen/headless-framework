// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Permissions;

/// <summary>EF Core entity type configuration for <see cref="PermissionDefinitionRecord"/>.</summary>
/// <param name="options">Storage options supplying the table name and schema.</param>
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
        b.Property(x => x.GroupName).HasMaxLength(PermissionDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(PermissionDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.ParentName).HasMaxLength(PermissionDefinitionRecordConstants.NameMaxLength);
        b.Property(x => x.DisplayName)
            .HasMaxLength(PermissionDefinitionRecordConstants.DisplayNameMaxLength)
            .IsRequired();
        b.Property(x => x.Providers).HasMaxLength(PermissionDefinitionRecordConstants.ProvidersMaxLength);
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
