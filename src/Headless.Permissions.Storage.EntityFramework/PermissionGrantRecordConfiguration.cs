// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
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

        b.HasIndex(x => new
            {
                x.TenantId,
                x.Name,
                x.ProviderName,
                x.ProviderKey,
            })
            .IsUnique();
    }
}
