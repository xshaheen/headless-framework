// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Permissions.Storage.EntityFramework;

[PublicAPI]
public sealed class PermissionsDbContext(DbContextOptions options) : DbContext(options)
{
    public static string DefaultSchema { get; set; } = "permissions";
    public static string DefaultPermissionGrantTableName { get; set; } = "PermissionGrants";
    public static string DefaultPermissionDefinitionTableName { get; set; } = "PermissionDefinitions";
    public static string DefaultPermissionGroupDefinitionTableName { get; set; } = "PermissionGroupDefinitions";

    public required DbSet<PermissionGrantRecord> PermissionGrants { get; init; }

    public required DbSet<PermissionDefinitionRecord> PermissionDefinitions { get; init; }

    public required DbSet<PermissionGroupDefinitionRecord> PermissionGroupDefinitions { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PermissionGrantRecord>(b =>
        {
            b.ToTable(DefaultPermissionGrantTableName, DefaultSchema);
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
            b.ToTable(DefaultPermissionGroupDefinitionTableName, DefaultSchema);
            b.TryConfigureExtraProperties();
            b.Property(x => x.Name).HasMaxLength(PermissionGroupDefinitionRecordConstants.NameMaxLength).IsRequired();
            b.Property(x => x.DisplayName)
                .HasMaxLength(PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength)
                .IsRequired();
            b.HasIndex(x => new { x.Name }).IsUnique();
        });

        modelBuilder.Entity<PermissionDefinitionRecord>(b =>
        {
            b.ToTable(DefaultPermissionDefinitionTableName, DefaultSchema);
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
