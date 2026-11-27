// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SqlServerFeaturesEntityFrameworkModelTests : TestBase
{
    [Fact]
    public void should_emit_binary_collation_for_feature_provider_keys()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<FeaturesDbContext>()
            .UseSqlServer("Server=localhost;Database=FeaturesModel")
            .UseApplicationServiceProvider(services)
            .Options;
        using var context = new FeaturesDbContext(options);

        var script = context.Database.GenerateCreateScript();

        script.Should().Contain("[ProviderKey] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL");
    }

    private sealed class FeaturesDbContext(DbContextOptions<FeaturesDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessFeatures(this);
        }
    }
}
