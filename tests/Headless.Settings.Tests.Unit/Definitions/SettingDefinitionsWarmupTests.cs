// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Security;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Definitions;

/// <summary>
/// The setting definitions warm-up: it loads the dynamic store behind readiness only when the dynamic store is enabled.
/// </summary>
public sealed class SettingDefinitionsWarmupTests : TestBase
{
    private readonly IDynamicSettingDefinitionStore _store = Substitute.For<IDynamicSettingDefinitionStore>();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();

    public SettingDefinitionsWarmupTests()
    {
        _services.GetService(typeof(IDynamicSettingDefinitionStore)).Returns(_store);
    }

    [Fact]
    public void should_be_named_setting_definitions()
    {
        IWarmup sut = _CreateSut(dynamicStore: true);

        sut.Name.Should().Be("SettingDefinitions");
    }

    [Fact]
    public async Task should_load_the_dynamic_store_and_gate_readiness_when_the_dynamic_store_is_enabled()
    {
        IWarmup sut = _CreateSut(dynamicStore: true);

        await sut.WarmUpAsync(AbortToken);

        sut.GatesReadiness.Should().BeTrue();
        await _store.Received(1).GetAllAsync(AbortToken);
    }

    [Fact]
    public async Task should_neither_resolve_the_store_nor_gate_readiness_when_the_dynamic_store_is_disabled()
    {
        IWarmup sut = _CreateSut(dynamicStore: false);

        await sut.WarmUpAsync(AbortToken);

        sut.GatesReadiness.Should().BeFalse();
        _services.DidNotReceive().GetService(typeof(IDynamicSettingDefinitionStore));
    }

    [Fact]
    public async Task should_hold_readiness_until_the_dynamic_store_has_loaded_when_enabled()
    {
        // given
        var load = new TaskCompletionSource<IReadOnlyList<SettingDefinition>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _store.GetAllAsync(Arg.Any<CancellationToken>()).Returns(load.Task);
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
        await _store.Received(1).GetAllAsync(Arg.Any<CancellationToken>());

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
        await _store.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    private SettingDefinitionsWarmup _CreateSut(bool dynamicStore) =>
        new(_services, Options.Create(new SettingManagementOptions { IsDynamicSettingStoreEnabled = dynamicStore }));

    private ServiceProvider _BuildProvider(bool dynamicStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "WarmupPassPhrase123";
            options.DefaultSalt = [.. "WarmupSalt"u8];
        });
        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureManagement(options => options.IsDynamicSettingStoreEnabled = dynamicStore);
            setup.UseEntityFramework<WarmupTestDbContext>();
        });
        services.Replace(ServiceDescriptor.Singleton(_store));

        return services.BuildServiceProvider();
    }

    // The warm-up runner is the one initializer that follows the host lifecycle; AddHeadlessSettings registers it.
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
