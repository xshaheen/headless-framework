// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

public sealed class FeaturesEntityStartupValidatorTests(FeaturesTestFixture fixture) : FeaturesTestBase(fixture)
{
    [Fact]
    public async Task should_fail_startup_when_shared_dbcontext_does_not_include_features_entities()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        // AddHeadlessFeatures auto-registers the management core, whose initialization hosted
        // service requires TimeProvider — register it so startup reaches the entity startup validator
        // rather than failing to activate the hosted service first.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContextFactory<MissingFeaturesEntityDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );
        // The value store caches every read, and the host refuses to start without a registered cache.
        builder.Services.AddHeadlessCaching(setup =>
            setup.UseRedis(options => options.ConnectionMultiplexer = Fixture.Multiplexer)
        );
        builder.Services.AddHeadlessFeatures(setup => setup.UseEntityFramework<MissingFeaturesEntityDbContext>());
        using var host = builder.Build();

        // when
        var action = async () => await host.StartAsync(AbortToken);

        // then
        await action
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*FeatureValueRecord*modelBuilder.AddHeadlessFeatures*");
    }

    private sealed class MissingFeaturesEntityDbContext(DbContextOptions<MissingFeaturesEntityDbContext> options)
        : DbContext(options);
}
