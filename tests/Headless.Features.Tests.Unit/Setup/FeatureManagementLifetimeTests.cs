// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Features;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Setup;

public sealed class FeatureManagementLifetimeTests : TestBase
{
    [Fact]
    public async Task should_share_one_feature_manager_and_handler_across_requests()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        // The real definition manager pulls in the dynamic store's lock and bus; a substitute registered first wins
        // because AddHeadlessFeatures registers the manager with TryAddSingleton.
        services.AddSingleton(Substitute.For<IFeatureDefinitionManager>());
        services.AddSingleton(Substitute.For<ICache>());
        services.AddDbContextFactory<FeaturesDbContext>(static options => options.UseSqlite("Data Source=:memory:"));
        services.AddHeadlessFeatures(setup => setup.UseEntityFramework<FeaturesDbContext>());

        // when - resolved the way the authorization middleware does, from a request scope
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        // then
        first
            .ServiceProvider.GetRequiredService<IFeatureManager>()
            .Should()
            .BeSameAs(second.ServiceProvider.GetRequiredService<IFeatureManager>());
        _FeatureHandler(first.ServiceProvider).Should().BeSameAs(_FeatureHandler(second.ServiceProvider));
    }

    private static IAuthorizationHandler _FeatureHandler(IServiceProvider provider)
    {
        return provider
            .GetServices<IAuthorizationHandler>()
            .Should()
            .ContainSingle(handler => handler is FeatureRequirementHandler)
            .Which;
    }

    private sealed class FeaturesDbContext(DbContextOptions<FeaturesDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessFeatures(this);
        }
    }
}
