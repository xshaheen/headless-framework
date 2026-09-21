// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Permissions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Definitions;

/// <summary>
/// The dynamic store is a singleton, so every service it takes must outlive a scope. A host that registers
/// permissions alongside messaging fails to start under <c>ValidateScopes</c> if the publishing facade is not
/// root-resolvable.
/// </summary>
public sealed class DynamicPermissionDefinitionStoreLifetimeTests : TestBase
{
    [Fact]
    public void should_register_the_dynamic_store_as_a_singleton()
    {
        var services = new ServiceCollection();

        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<LifetimeProbeDbContext>());

        services
            .Single(descriptor => descriptor.ServiceType == typeof(IDynamicPermissionDefinitionStore))
            .Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task should_resolve_the_dynamic_store_from_the_root_of_a_scope_validating_provider()
    {
        // given — the real messaging registration supplies IBus; every other dependency is a stand-in
        // registered with the store's own singleton lifetime.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(_ => { });
        services.AddSingleton(Substitute.For<IPermissionDefinitionRecordRepository>());
        services.AddSingleton(Substitute.For<IStaticPermissionDefinitionStore>());
        services.AddSingleton(Substitute.For<IPermissionDefinitionSerializer>());
        services.AddSingleton(Substitute.For<ICache>());
        services.AddSingleton(Substitute.For<IDistributedLock>());
        services.AddSingleton(Substitute.For<IGuidGenerator>());
        services.AddSingleton(Substitute.For<IHostIdentityAccessor>());
        services.AddSingleton(Options.Create(new PermissionManagementOptions()));
        services.AddSingleton(Options.Create(new PermissionManagementProvidersOptions()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDynamicPermissionDefinitionStore, DynamicPermissionDefinitionStore>();

        // when
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        provider.GetRequiredService<IDynamicPermissionDefinitionStore>().Should().NotBeNull();
    }

    private sealed class LifetimeProbeDbContext(DbContextOptions<LifetimeProbeDbContext> options) : DbContext(options);
}
