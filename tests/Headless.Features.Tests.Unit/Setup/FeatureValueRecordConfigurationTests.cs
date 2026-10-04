// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Hosting.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests.Setup;

public sealed class FeatureValueRecordConfigurationTests
{
    [Fact]
    public void should_enforce_uniqueness_for_both_null_and_non_null_provider_keys()
    {
        // given — PostgreSQL/SQLite treat NULLs as distinct, so the NULL-key scope needs its own index
        using var context = _CreateContext(StorageNamingStyle.PascalCase);

        // when
        var entity = context.Model.FindEntityType(typeof(FeatureValueRecord));
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
        script.Should().Contain("IX_FeatureValues_Name_ProviderName_ProviderKey");
        script.Should().Contain("IX_FeatureValues_Name_ProviderName_NullProviderKey");
    }

    [Fact]
    public void should_name_every_object_in_snake_case_when_the_style_is_snake_case()
    {
        // given
        using var context = _CreateContext(StorageNamingStyle.SnakeCase);

        // when
        var entity = context.Model.FindEntityType(typeof(FeatureValueRecord))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

        // then
        entity.GetTableName().Should().Be("feature_values");
        entity
            .GetProperties()
            .Select(p => p.GetColumnName(table))
            .Should()
            .BeEquivalentTo("id", "name", "value", "provider_name", "provider_key", "created_at", "updated_at");
        entity.FindPrimaryKey()!.GetName().Should().Be("pk_feature_values");
        entity
            .GetIndexes()
            .Select(i => (i.GetDatabaseName(), i.GetFilter()))
            .Should()
            .BeEquivalentTo([
                ("ix_feature_values_name_provider_name_provider_key", "\"provider_key\" IS NOT NULL"),
                ("ix_feature_values_name_provider_name_null_provider_key", "\"provider_key\" IS NULL"),
                ("ix_feature_values_provider_name_provider_key", (string?)null),
            ]);
    }

    // EF caches one model per context type, so each naming style needs its own context type.
    private static FeaturesModelDbContext _CreateContext(StorageNamingStyle style)
    {
        return style == StorageNamingStyle.SnakeCase
            ? new SnakeCaseFeaturesModelDbContext(
                new DbContextOptionsBuilder<SnakeCaseFeaturesModelDbContext>().UseSqlite("DataSource=:memory:").Options
            )
            : new PascalCaseFeaturesModelDbContext(
                new DbContextOptionsBuilder<PascalCaseFeaturesModelDbContext>().UseSqlite("DataSource=:memory:").Options
            );
    }

    private abstract class FeaturesModelDbContext(DbContextOptions options, StorageNamingStyle style)
        : DbContext(options)
    {
        public DbSet<FeatureValueRecord> FeatureValues => Set<FeatureValueRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessFeatures(new FeaturesStorageOptions(), style);
        }
    }

    private sealed class PascalCaseFeaturesModelDbContext(DbContextOptions<PascalCaseFeaturesModelDbContext> options)
        : FeaturesModelDbContext(options, StorageNamingStyle.PascalCase);

    private sealed class SnakeCaseFeaturesModelDbContext(DbContextOptions<SnakeCaseFeaturesModelDbContext> options)
        : FeaturesModelDbContext(options, StorageNamingStyle.SnakeCase);
}
