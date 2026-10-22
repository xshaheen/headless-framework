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
        using var host = _BuildHost(dynamicStore: true);

        // when
        await host.StartAsync(AbortToken);
        var warm = host.Services.WaitForWarmupAsync(AbortToken);

        // then
        warm.IsCompleted.Should().BeFalse();

        load.SetResult([]);
        await warm.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await _store.Received(1).GetAllAsync(Arg.Any<CancellationToken>());

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_not_hold_readiness_or_load_the_store_when_the_dynamic_store_is_disabled()
    {
        // given
        using var host = _BuildHost(dynamicStore: false);

        // when
        await host.StartAsync(AbortToken);

        // then
        await host.Services.WaitForWarmupAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        await host.StopAsync(AbortToken);
        await _store.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    private SettingDefinitionsWarmup _CreateSut(bool dynamicStore) =>
        new(_services, Options.Create(new SettingManagementOptions { IsDynamicSettingStoreEnabled = dynamicStore }));

    private IHost _BuildHost(bool dynamicStore)
    {
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
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

        // The warm-up is under test, not the storage wiring: the cache and the EF context factory the required-service
        // check asks for would not be reached with the store replaced.
        services.RemoveAll<IHeadlessStartupValidator>();

        return builder.Build();
    }

    private sealed class WarmupTestDbContext(DbContextOptions<WarmupTestDbContext> options) : DbContext(options);
}
