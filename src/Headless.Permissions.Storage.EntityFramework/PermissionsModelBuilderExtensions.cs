// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Storage.EntityFramework;

[PublicAPI]
public static class PermissionsModelBuilderExtensions
{
    public static void AddPermissionsConfiguration(this ModelBuilder modelBuilder, DbContext context)
    {
        Argument.IsNotNull(context);

        var options = context.GetService<IOptions<PermissionsStorageOptions>>().Value;
        modelBuilder.AddPermissionsConfiguration(options);
    }

    public static void AddPermissionsConfiguration(this ModelBuilder modelBuilder, PermissionsStorageOptions options)
    {
        Argument.IsNotNull(options);

        modelBuilder.Entity<PermissionGrantRecord>(b =>
        {
            b.ToTable(options.PermissionGrantsTableName, options.Schema);
            b.Property(x => x.Name).HasMaxLength(PermissionGrantRecordConstants.NameMaxLength).IsRequired();

            b.Property(x => x.ProviderName)
                .HasMaxLength(PermissionGrantRecordConstants.ProviderNameMaxLength)
                .IsRequired();

            b.Property(x => x.ProviderKey)
                .HasMaxLength(PermissionGrantRecordConstants.ProviderKeyMaxLength)
                .IsRequired();

            b.Property(x => x.TenantId)
                .HasMaxLength(PermissionGrantRecordConstants.TenantIdMaxLength)
                .IsRequired(false);

            b.HasIndex(x => new
                {
                    x.TenantId,
                    x.Name,
                    x.ProviderName,
                    x.ProviderKey,
                })
                .IsUnique();
        });

        modelBuilder.Entity<PermissionGroupDefinitionRecord>(b =>
        {
            b.ToTable(options.PermissionGroupDefinitionsTableName, options.Schema);
            b.TryConfigureExtraProperties();
            b.Property(x => x.Name).HasMaxLength(PermissionGroupDefinitionRecordConstants.NameMaxLength).IsRequired();

            b.Property(x => x.DisplayName)
                .HasMaxLength(PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength)
                .IsRequired();

            b.HasIndex(x => new { x.Name }).IsUnique();
        });

        modelBuilder.Entity<PermissionDefinitionRecord>(b =>
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
        });
    }
}
