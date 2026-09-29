// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Permissions;
using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests.Setup;

public sealed class PermissionGrantRecordConfigurationTests
{
    [Fact]
    public void should_enforce_uniqueness_for_both_null_and_non_null_tenants()
    {
        // given — PostgreSQL/SQLite treat NULLs as distinct, so host (NULL tenant) grants need their own index
        using var context = _CreateContext(StorageNamingStyle.PascalCase);

        // when
        var entity = context.Model.FindEntityType(typeof(PermissionGrantRecord));
        var uniqueIndexes = entity!.GetIndexes().Where(x => x.IsUnique).ToList();

        // then
        uniqueIndexes.Should().HaveCount(2);
        uniqueIndexes
            .Should()
            .ContainSingle(x =>
                x.Properties.Select(p => p.Name)
                    .SequenceEqual(new[] { "TenantId", "Name", "ProviderName", "ProviderKey" })
                && x.GetFilter() == "\"TenantId\" IS NOT NULL"
            );
        uniqueIndexes
            .Should()
            .ContainSingle(x =>
                x.Properties.Select(p => p.Name).SequenceEqual(new[] { "Name", "ProviderName", "ProviderKey" })
                && x.GetFilter() == "\"TenantId\" IS NULL"
            );
    }

    [Fact]
    public void should_emit_both_unique_indexes_in_the_create_script()
    {
        // given
        using var context = _CreateContext(StorageNamingStyle.PascalCase);

        // when
        var script = context.Database.GenerateCreateScript();

        // then
        script.Should().Contain("IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey");
        script.Should().Contain("IX_PermissionGrants_Name_ProviderName_ProviderKey_NoTenant");
    }

    [Fact]
    public void should_name_every_object_in_snake_case_when_the_style_is_snake_case()
    {
        // given
        using var context = _CreateContext(StorageNamingStyle.SnakeCase);

        // when
        var entity = context.Model.FindEntityType(typeof(PermissionGrantRecord))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

        // then
        entity.GetTableName().Should().Be("permission_grants");
        entity
            .GetProperties()
            .Select(p => p.GetColumnName(table))
            .Should()
            .BeEquivalentTo(
                "id",
                "name",
                "provider_name",
                "provider_key",
                "tenant_id",
                "is_granted",
                "created_at",
                "updated_at"
            );
        entity.FindPrimaryKey()!.GetName().Should().Be("pk_permission_grants");
        entity
            .GetIndexes()
            .Select(i => (i.GetDatabaseName(), i.GetFilter()))
            .Should()
            .BeEquivalentTo([
                ("ix_permission_grants_tenant_id_name_provider_name_provider_key", "\"tenant_id\" IS NOT NULL"),
                ("ix_permission_grants_name_provider_name_provider_key_no_tenant", "\"tenant_id\" IS NULL"),
            ]);
    }

    // EF caches one model per context type, so each naming style needs its own context type.
    private static PermissionsModelDbContext _CreateContext(StorageNamingStyle style)
    {
        return style == StorageNamingStyle.SnakeCase
            ? new SnakeCasePermissionsModelDbContext(
                new DbContextOptionsBuilder<SnakeCasePermissionsModelDbContext>()
                    .UseSqlite("DataSource=:memory:")
                    .Options
            )
            : new PascalCasePermissionsModelDbContext(
                new DbContextOptionsBuilder<PascalCasePermissionsModelDbContext>()
                    .UseSqlite("DataSource=:memory:")
                    .Options
            );
    }

    private abstract class PermissionsModelDbContext(DbContextOptions options, StorageNamingStyle style)
        : DbContext(options)
    {
        public DbSet<PermissionGrantRecord> PermissionGrants => Set<PermissionGrantRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessPermissions(new PermissionsStorageOptions(), style);
        }
    }

    private sealed class PascalCasePermissionsModelDbContext(
        DbContextOptions<PascalCasePermissionsModelDbContext> options
    ) : PermissionsModelDbContext(options, StorageNamingStyle.PascalCase);

    private sealed class SnakeCasePermissionsModelDbContext(
        DbContextOptions<SnakeCasePermissionsModelDbContext> options
    ) : PermissionsModelDbContext(options, StorageNamingStyle.SnakeCase);
}
