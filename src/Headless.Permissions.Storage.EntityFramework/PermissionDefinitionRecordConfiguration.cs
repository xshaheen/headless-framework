// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Permissions;

internal sealed class PermissionDefinitionRecordConfiguration(PermissionsStorageOptions options)
    : IEntityTypeConfiguration<PermissionDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<PermissionDefinitionRecord> b)
    {
        b.ToTable(options.PermissionDefinitionsTableName, options.Schema);
        b.TryConfigureExtraProperties();
        b.Property(x => x.GroupName).HasMaxLength(PermissionDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.Name).HasMaxLength(PermissionDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.ParentName).HasMaxLength(PermissionDefinitionRecordConstants.NameMaxLength);
        b.Property(x => x.DisplayName)
            .HasMaxLength(PermissionDefinitionRecordConstants.DisplayNameMaxLength)
            .IsRequired();
        b.Property(x => x.Providers).HasMaxLength(PermissionDefinitionRecordConstants.ProvidersMaxLength);
        b.HasIndex(x => new { x.Name }).IsUnique();
        b.HasIndex(x => new { x.GroupName });
    }
}
