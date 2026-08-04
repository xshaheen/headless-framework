// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tests.Setup;

public sealed class FeatureValueRecordConfigurationTests
{
    [Fact]
    public void should_enforce_uniqueness_for_both_null_and_non_null_provider_keys()
    {
        // given — PostgreSQL/SQLite treat NULLs as distinct, so the NULL-key scope needs its own index
        using var context = _CreateContext();

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
        using var context = _CreateContext();

        // when
        var script = context.Database.GenerateCreateScript();

        // then
        script.Should().Contain("IX_FeatureValues_Name_ProviderName_ProviderKey");
        script.Should().Contain("IX_FeatureValues_Name_ProviderName_NullProviderKey");
    }

    private static FeaturesModelDbContext _CreateContext()
    {
        return new FeaturesModelDbContext(
            new DbContextOptionsBuilder<FeaturesModelDbContext>().UseSqlite("DataSource=:memory:").Options
        );
    }

    private sealed class FeaturesModelDbContext(DbContextOptions<FeaturesModelDbContext> options) : DbContext(options)
    {
        public DbSet<FeatureValueRecord> FeatureValues => Set<FeatureValueRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessFeatures(new FeaturesStorageOptions());
        }
    }
}
