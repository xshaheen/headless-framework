// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Permissions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tests.Setup;

public sealed class PermissionGrantRecordConfigurationTests
{
    [Fact]
    public void should_enforce_uniqueness_for_both_null_and_non_null_tenants()
    {
        // given — PostgreSQL/SQLite treat NULLs as distinct, so host (NULL tenant) grants need their own index
        using var context = _CreateContext();

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
        using var context = _CreateContext();

        // when
        var script = context.Database.GenerateCreateScript();

        // then
        script.Should().Contain("IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey");
        script.Should().Contain("IX_PermissionGrants_Name_ProviderName_ProviderKey_NullTenantId");
    }

    private static PermissionsModelDbContext _CreateContext()
    {
        return new PermissionsModelDbContext(
            new DbContextOptionsBuilder<PermissionsModelDbContext>().UseSqlite("DataSource=:memory:").Options
        );
    }

    private sealed class PermissionsModelDbContext(DbContextOptions<PermissionsModelDbContext> options)
        : DbContext(options)
    {
        public DbSet<PermissionGrantRecord> PermissionGrants => Set<PermissionGrantRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessPermissions(new PermissionsStorageOptions());
        }
    }
}
