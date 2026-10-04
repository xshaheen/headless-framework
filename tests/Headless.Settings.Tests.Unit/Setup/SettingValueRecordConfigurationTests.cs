// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests.Setup;

public sealed class SettingValueRecordConfigurationTests
{
    [Fact]
    public void should_enforce_uniqueness_for_both_null_and_non_null_provider_keys()
    {
        // given — PostgreSQL/SQLite treat NULLs as distinct, so the NULL-key scope needs its own index
        using var context = _CreateContext(StorageNamingStyle.PascalCase);

        // when
        var entity = context.Model.FindEntityType(typeof(SettingValueRecord));
        var uniqueIndexes = entity!.GetIndexes().Where(x => x.IsUnique).ToList();

        // then
        uniqueIndexes.Should().HaveCount(2);
        uniqueIndexes
            .Should()
            .ContainSingle(x =>
                x.Properties.Select(p => p.Name).SequenceEqual(new[] { "Name", "ProviderName", "ProviderKey" })
                && x.GetFilter() == "\"ProviderKey\" IS NOT NULL"
            );
        uniqueIndexes
            .Should()
            .ContainSingle(x =>
                x.Properties.Select(p => p.Name).SequenceEqual(new[] { "Name", "ProviderName" })
                && x.GetFilter() == "\"ProviderKey\" IS NULL"
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
        script.Should().Contain("IX_SettingValues_Name_ProviderName_ProviderKey");
        script.Should().Contain("IX_SettingValues_Name_ProviderName_NullProviderKey");
    }

    [Fact]
    public void should_name_every_object_in_snake_case_when_the_style_is_snake_case()
    {
        // given
        using var context = _CreateContext(StorageNamingStyle.SnakeCase);

        // when
        var entity = context.Model.FindEntityType(typeof(SettingValueRecord))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

        // then
        entity.GetTableName().Should().Be("setting_values");
        entity
            .GetProperties()
            .Select(p => p.GetColumnName(table))
            .Should()
            .BeEquivalentTo("id", "name", "value", "provider_name", "provider_key", "created_at", "updated_at");
        entity.FindPrimaryKey()!.GetName().Should().Be("pk_setting_values");
        entity
            .GetIndexes()
            .Select(i => (i.GetDatabaseName(), i.GetFilter()))
            .Should()
            .BeEquivalentTo([
                ("ix_setting_values_name_provider_name_provider_key", "\"provider_key\" IS NOT NULL"),
                ("ix_setting_values_name_provider_name_null_provider_key", "\"provider_key\" IS NULL"),
            ]);
    }

    // EF caches one model per context type, so each naming style needs its own context type.
    private static SettingsModelDbContext _CreateContext(StorageNamingStyle style)
    {
        return style == StorageNamingStyle.SnakeCase
            ? new SnakeCaseSettingsModelDbContext(
                new DbContextOptionsBuilder<SnakeCaseSettingsModelDbContext>().UseSqlite("DataSource=:memory:").Options
            )
            : new PascalCaseSettingsModelDbContext(
                new DbContextOptionsBuilder<PascalCaseSettingsModelDbContext>().UseSqlite("DataSource=:memory:").Options
            );
    }

    private abstract class SettingsModelDbContext(DbContextOptions options, StorageNamingStyle style)
        : DbContext(options)
    {
        public DbSet<SettingValueRecord> SettingValues => Set<SettingValueRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessSettings(new SettingsStorageOptions(), style);
        }
    }

    private sealed class PascalCaseSettingsModelDbContext(DbContextOptions<PascalCaseSettingsModelDbContext> options)
        : SettingsModelDbContext(options, StorageNamingStyle.PascalCase);

    private sealed class SnakeCaseSettingsModelDbContext(DbContextOptions<SnakeCaseSettingsModelDbContext> options)
        : SettingsModelDbContext(options, StorageNamingStyle.SnakeCase);
}
