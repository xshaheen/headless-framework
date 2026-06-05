// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.Permissions;

internal sealed class PermissionGroupDefinitionRecordConfiguration(PermissionsStorageOptions options)
    : IEntityTypeConfiguration<PermissionGroupDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<PermissionGroupDefinitionRecord> b)
    {
        b.ToTable(options.PermissionGroupDefinitionsTableName, options.Schema);
        b.TryConfigureExtraProperties();
        b.Property(x => x.Name).HasMaxLength(PermissionGroupDefinitionRecordConstants.NameMaxLength).IsRequired();
        b.Property(x => x.DisplayName)
            .HasMaxLength(PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength)
            .IsRequired();
        b.HasIndex(x => new { x.Name }).IsUnique();
    }
}
