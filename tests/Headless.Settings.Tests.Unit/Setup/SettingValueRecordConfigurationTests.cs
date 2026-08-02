// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;
using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tests.Setup;

public sealed class SettingValueRecordConfigurationTests
{
    [Fact]
    public void should_enforce_uniqueness_for_both_null_and_non_null_provider_keys()
    {
        // given — PostgreSQL/SQLite treat NULLs as distinct, so the NULL-key scope needs its own index
        using var context = _CreateContext();

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
        using var context = _CreateContext();

        // when
        var script = context.Database.GenerateCreateScript();

        // then
        script.Should().Contain("IX_SettingValues_Name_ProviderName_ProviderKey");
        script.Should().Contain("IX_SettingValues_Name_ProviderName_NullProviderKey");
    }

    private static SettingsModelDbContext _CreateContext()
    {
        return new SettingsModelDbContext(
            new DbContextOptionsBuilder<SettingsModelDbContext>().UseSqlite("DataSource=:memory:").Options
        );
    }

    private sealed class SettingsModelDbContext(DbContextOptions<SettingsModelDbContext> options) : DbContext(options)
    {
        public DbSet<SettingValueRecord> SettingValues => Set<SettingValueRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessSettings(new SettingsStorageOptions());
        }
    }
}
