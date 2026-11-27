// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

/// <summary>
/// A custom value provider lands where its registration asks, whether the app registers it before or after
/// <c>AddHeadlessFeatures</c>.
/// </summary>
public sealed class FeatureValueProviderChainTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_place_provider_between_edition_and_tenant_when_added_below_tenant(bool beforeAddHeadlessFeatures)
    {
        // given
        var services = new ServiceCollection();

        // when
        if (beforeAddHeadlessFeatures)
        {
            services.AddFeatureValueProviderBelow<PlanFeatureValueProvider, TenantFeatureValueProvider>();
            services.AddHeadlessFeatures(setup => setup.UseEntityFramework<ChainTestDbContext>());
        }
        else
        {
            services.AddHeadlessFeatures(setup => setup.UseEntityFramework<ChainTestDbContext>());
            services.AddFeatureValueProviderBelow<PlanFeatureValueProvider, TenantFeatureValueProvider>();
        }

        using var serviceProvider = services.BuildServiceProvider();

        // then - the list runs lowest priority first
        _ValueProviders(serviceProvider)
            .Should()
            .Equal(
                typeof(DefaultValueFeatureValueProvider),
                typeof(EditionFeatureValueProvider),
                typeof(PlanFeatureValueProvider),
                typeof(TenantFeatureValueProvider)
            );
    }

    [Fact]
    public void should_put_provider_on_top_when_added_before_add_headless_features()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddFeatureValueProvider<PlanFeatureValueProvider>();
        services.AddHeadlessFeatures(setup => setup.UseEntityFramework<ChainTestDbContext>());

        using var serviceProvider = services.BuildServiceProvider();

        // then
        _ValueProviders(serviceProvider).Should().HaveCount(4).And.EndWith(typeof(PlanFeatureValueProvider));
    }

    [Fact]
    public void should_throw_when_the_higher_provider_is_not_in_the_chain()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessFeatures(setup => setup.UseEntityFramework<ChainTestDbContext>());
        services.AddFeatureValueProviderBelow<PlanFeatureValueProvider, UnregisteredFeatureValueProvider>();

        using var serviceProvider = services.BuildServiceProvider();

        // when
        var act = () => _ValueProviders(serviceProvider);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(UnregisteredFeatureValueProvider)}*");
    }

    [Fact]
    public void should_throw_when_a_provider_is_placed_below_itself()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddFeatureValueProviderBelow<PlanFeatureValueProvider, PlanFeatureValueProvider>();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    private static List<Type> _ValueProviders(IServiceProvider serviceProvider)
    {
        return
        [
            .. serviceProvider.GetRequiredService<IOptions<FeatureManagementProvidersOptions>>().Value.ValueProviders,
        ];
    }

    private class NullFeatureValueProvider(string name) : IFeatureValueReadProvider
    {
        public string Name { get; } = name;

        public Task<IAsyncDisposable> HandleContextAsync(
            string providerName,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IAsyncDisposable>(NullAsyncDisposable.Instance);
        }

        public Task<string?> GetOrDefaultAsync(
            FeatureDefinition feature,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<string?>(null);
        }

        public Task<List<FeatureValue>> GetAllAsync(
            FeatureDefinition[] features,
            string? providerKey = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(features.Select(feature => new FeatureValue(feature.Name, null)).ToList());
        }
    }

    private sealed class PlanFeatureValueProvider() : NullFeatureValueProvider("Plan");

    private sealed class UnregisteredFeatureValueProvider() : NullFeatureValueProvider("Unregistered");

    private sealed class NullAsyncDisposable : IAsyncDisposable
    {
        public static readonly NullAsyncDisposable Instance = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ChainTestDbContext(DbContextOptions<ChainTestDbContext> options) : DbContext(options);
}
