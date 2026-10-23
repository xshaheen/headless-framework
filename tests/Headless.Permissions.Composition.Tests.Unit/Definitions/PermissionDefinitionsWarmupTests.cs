// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Definitions;

/// <summary>
/// The permission definitions warm-up: it loads the dynamic store behind readiness only when the dynamic store is enabled.
/// </summary>
public sealed class PermissionDefinitionsWarmupTests : TestBase
{
    private readonly IDynamicPermissionDefinitionStore _store = Substitute.For<IDynamicPermissionDefinitionStore>();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();

    public PermissionDefinitionsWarmupTests()
    {
        _services.GetService(typeof(IDynamicPermissionDefinitionStore)).Returns(_store);
    }

    [Fact]
    public void should_be_named_permission_definitions()
    {
        IWarmup sut = _CreateSut(dynamicStore: true);

        sut.Name.Should().Be("PermissionDefinitions");
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
        _services.DidNotReceive().GetService(typeof(IDynamicPermissionDefinitionStore));
    }

    [Fact]
    public async Task should_hold_readiness_until_the_dynamic_store_has_loaded_when_enabled()
    {
        // given
        var load = new TaskCompletionSource<IReadOnlyList<PermissionGroupDefinition>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _store.GetGroupsAsync(Arg.Any<CancellationToken>()).Returns(load.Task);
        await using var host = _BuildHost(dynamicStore: true);

        // when
        await host.StartAsync(AbortToken);
        var warm = host.Services.WaitForWarmupAsync(AbortToken);

        // then
        warm.IsCompleted.Should().BeFalse();

        load.SetResult([]);
        await warm.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await _store.Received(1).GetGroupsAsync(Arg.Any<CancellationToken>());

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_not_hold_readiness_or_load_the_store_when_the_dynamic_store_is_disabled()
    {
        // given
        await using var host = _BuildHost(dynamicStore: false);

        // when
        await host.StartAsync(AbortToken);

        // then
        await host.Services.WaitForWarmupAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(1), AbortToken);
        await host.StopAsync(AbortToken);
        await _store.DidNotReceive().GetGroupsAsync(Arg.Any<CancellationToken>());
    }

    private PermissionDefinitionsWarmup _CreateSut(bool dynamicStore) =>
        new(
            _services,
            Options.Create(new PermissionManagementOptions { IsDynamicPermissionStoreEnabled = dynamicStore })
        );

    private LifecycleHost _BuildHost(bool dynamicStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureManagement(options => options.IsDynamicPermissionStoreEnabled = dynamicStore);
            setup.UseEntityFramework<WarmupTestDbContext>();
        });
        services.Replace(ServiceDescriptor.Singleton(_store));

        // The warm-up is under test, not the storage wiring: the cache and the EF context factory the required-service
        // check asks for would not be reached with the store replaced.
        services.RemoveAll<IHeadlessStartupValidator>();

        return new LifecycleHost(services.BuildServiceProvider());
    }

    // Runs the lifecycle services a generic host would, without the hosting package this project does not reference.
    private sealed class LifecycleHost(ServiceProvider services) : IAsyncDisposable
    {
        private readonly List<IHostedLifecycleService> _lifecycle =
        [
            .. services.GetServices<IHostedService>().OfType<IHostedLifecycleService>(),
        ];

        public IServiceProvider Services => services;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var service in _lifecycle)
            {
                await service.StartingAsync(cancellationToken);
            }

            foreach (var service in _lifecycle)
            {
                await service.StartAsync(cancellationToken);
            }

            foreach (var service in _lifecycle)
            {
                await service.StartedAsync(cancellationToken);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var service in _lifecycle)
            {
                await service.StoppingAsync(cancellationToken);
            }

            foreach (var service in _lifecycle)
            {
                await service.StopAsync(cancellationToken);
            }

            foreach (var service in _lifecycle)
            {
                await service.StoppedAsync(cancellationToken);
            }
        }

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    private sealed class WarmupTestDbContext(DbContextOptions<WarmupTestDbContext> options) : DbContext(options);
}
