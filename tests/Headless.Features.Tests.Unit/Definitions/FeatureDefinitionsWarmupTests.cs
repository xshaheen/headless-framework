// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Definitions;

/// <summary>
/// The feature definitions warm-up: it loads the dynamic store behind readiness only when the dynamic store is enabled.
/// </summary>
public sealed class FeatureDefinitionsWarmupTests : TestBase
{
    private readonly IDynamicFeatureDefinitionStore _store = Substitute.For<IDynamicFeatureDefinitionStore>();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();

    public FeatureDefinitionsWarmupTests()
    {
        _services.GetService(typeof(IDynamicFeatureDefinitionStore)).Returns(_store);
    }

    [Fact]
    public void should_be_named_feature_definitions()
    {
        IWarmup sut = _CreateSut(dynamicStore: true);

        sut.Name.Should().Be("FeatureDefinitions");
    }

    [Fact]
    public async Task should_load_the_dynamic_store_and_gate_readiness_when_the_dynamic_store_is_enabled()
    {
        IWarmup sut = _CreateSut(dynamicStore: true);

        await sut.WarmUpAsync(AbortToken);

        sut.GatesReadiness.Should().BeTrue();
        await _store.Received(1).GetGroupsAsync(AbortToken);
    }

    [Fact]
    public async Task should_neither_resolve_the_store_nor_gate_readiness_when_the_dynamic_store_is_disabled()
    {
        IWarmup sut = _CreateSut(dynamicStore: false);

        await sut.WarmUpAsync(AbortToken);

        sut.GatesReadiness.Should().BeFalse();
        _services.DidNotReceive().GetService(typeof(IDynamicFeatureDefinitionStore));
    }

    [Fact]
    public async Task should_hold_readiness_until_the_dynamic_store_has_loaded_when_enabled()
    {
        // given
        var load = new TaskCompletionSource<IReadOnlyList<FeatureGroupDefinition>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _store.GetGroupsAsync(Arg.Any<CancellationToken>()).Returns(load.Task);
        await using var provider = _BuildProvider(dynamicStore: true);
        var runner = _Runner(provider);

        // when
        await runner.StartingAsync(AbortToken);
        await runner.StartedAsync(AbortToken);

        // then
        ((IInitializer)runner)
            .IsInitialized.Should()
            .BeFalse();

        load.SetResult([]);
        await ((IInitializer)runner)
            .WaitForInitializationAsync(AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await _store.Received(1).GetGroupsAsync(Arg.Any<CancellationToken>());

        await _StopAsync(runner);
    }

    [Fact]
    public async Task should_not_hold_readiness_or_load_the_store_when_the_dynamic_store_is_disabled()
    {
        // given
        await using var provider = _BuildProvider(dynamicStore: false);
        var runner = _Runner(provider);

        // when
        await runner.StartingAsync(AbortToken);
        await runner.StartedAsync(AbortToken);

        // then
        ((IInitializer)runner)
            .IsInitialized.Should()
            .BeTrue();
        await _StopAsync(runner);
        await _store.DidNotReceive().GetGroupsAsync(Arg.Any<CancellationToken>());
    }

    private FeatureDefinitionsWarmup _CreateSut(bool dynamicStore) =>
        new(_services, Options.Create(new FeatureManagementOptions { IsDynamicFeatureStoreEnabled = dynamicStore }));

    private ServiceProvider _BuildProvider(bool dynamicStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureManagement(options => options.IsDynamicFeatureStoreEnabled = dynamicStore);
            setup.UseEntityFramework<WarmupTestDbContext>();
        });
        services.Replace(ServiceDescriptor.Singleton(_store));

        return services.BuildServiceProvider();
    }

    // The warm-up runner is the one initializer that follows the host lifecycle; AddHeadlessFeatures registers it.
    private static IHostedLifecycleService _Runner(IServiceProvider provider) =>
        provider.GetServices<IInitializer>().OfType<IHostedLifecycleService>().Single();

    private static async Task _StopAsync(IHostedLifecycleService runner)
    {
        await runner.StoppingAsync(AbortToken);
        await runner.StopAsync(AbortToken);
        await runner.StoppedAsync(AbortToken);
    }

    private sealed class WarmupTestDbContext(DbContextOptions<WarmupTestDbContext> options) : DbContext(options);
}
