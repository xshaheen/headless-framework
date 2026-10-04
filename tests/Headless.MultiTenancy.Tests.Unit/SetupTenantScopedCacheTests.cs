// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class SetupTenantScopedCacheTests : TestBase
{
    private const string _Key = "user:1";

    [Fact]
    public async Task should_isolate_the_same_key_between_tenants()
    {
        // given
        await using var provider = _Build();
        var (cache, tenant) = _Resolve(provider);

        using (tenant.Change("acme"))
        {
            await cache.UpsertAsync(_Key, new UserProfile("acme-user"), expiration: null, AbortToken);
        }

        // when
        CacheValue<UserProfile> fromOtherTenant;

        using (tenant.Change("globex"))
        {
            fromOtherTenant = await cache.GetAsync(_Key, AbortToken);
        }

        // then
        fromOtherTenant.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_store_the_entry_under_the_tenant_scope_of_the_underlying_cache()
    {
        // given
        await using var provider = _Build();
        var (cache, tenant) = _Resolve(provider);

        // when
        using (tenant.Change("acme"))
        {
            await cache.UpsertAsync(_Key, new UserProfile("acme-user"), expiration: null, AbortToken);
        }

        // then
        var raw = await provider.GetRequiredService<ICache>().GetAsync<UserProfile>("t:acme:user:1", AbortToken);
        raw.Value.Should().Be(new UserProfile("acme-user"));
    }

    [Fact]
    public async Task should_refuse_the_operation_when_no_tenant_is_set()
    {
        // given
        await using var provider = _Build();
        var (cache, _) = _Resolve(provider);

        // when
        var action = () => cache.GetAsync(_Key, AbortToken).AsTask();

        // then
        await action.Should().ThrowAsync<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_refuse_a_tenant_id_containing_the_scope_separator()
    {
        // given
        await using var provider = _Build();
        var (cache, tenant) = _Resolve(provider);

        // when
        var action = async () =>
        {
            using (tenant.Change("acme:eu"))
            {
                await cache.GetAsync(_Key, AbortToken);
            }
        };

        // then
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*':'*");
    }

    [Fact]
    public async Task should_evict_only_the_current_tenant_when_removing_by_empty_prefix()
    {
        // given
        await using var provider = _Build();
        var (cache, tenant) = _Resolve(provider);

        foreach (var tenantId in new[] { "acme", "globex" })
        {
            using (tenant.Change(tenantId))
            {
                await cache.UpsertAsync(_Key, new UserProfile(tenantId), expiration: null, AbortToken);
                await cache.UpsertAsync("user:2", new UserProfile(tenantId), expiration: null, AbortToken);
            }
        }

        // when
        int removed;

        using (tenant.Change("acme"))
        {
            removed = await cache.RemoveByPrefixAsync("", AbortToken);
        }

        // then
        removed.Should().Be(2);

        using (tenant.Change("acme"))
        {
            (await cache.GetAsync(_Key, AbortToken)).HasValue.Should().BeFalse();
        }

        using (tenant.Change("globex"))
        {
            (await cache.GetAsync(_Key, AbortToken)).Value.Should().Be(new UserProfile("globex"));
            (await cache.GetAsync("user:2", AbortToken)).Value.Should().Be(new UserProfile("globex"));
        }
    }

    [Fact]
    public async Task should_win_over_the_open_generic_cache_when_caching_is_registered_later()
    {
        // given
        var services = new ServiceCollection();
        _AddTenantSource(services);
        services.AddTenantScopedCache<UserProfile>();
        services.AddHeadlessCaching(setup => setup.UseInMemory());

        await using var provider = services.BuildServiceProvider();

        // when
        var cache = provider.GetRequiredService<ICache<UserProfile>>();

        // then
        cache.Should().BeOfType<ScopedCache<UserProfile>>();
    }

    [Fact]
    public void should_be_a_no_op_when_called_twice_for_the_same_type()
    {
        // given
        var services = new ServiceCollection();
        services.AddTenantScopedCache<UserProfile>();

        // when
        services.AddTenantScopedCache<UserProfile>();

        // then
        services.Where(d => d.ServiceType == typeof(ICache<UserProfile>)).Should().ContainSingle();
    }

    [Fact]
    public void should_refuse_when_the_type_already_has_its_own_cache_registration()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICache<UserProfile>>());

        // when
        var action = () => services.AddTenantScopedCache<UserProfile>();

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    [Fact]
    public async Task should_fail_startup_when_no_tenant_source_is_registered()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        builder.Services.AddTenantScopedCache<UserProfile>();

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var act = async () =>
        {
            foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
            {
                await service.StartingAsync(AbortToken);
            }
        };

        // then
        (await act.Should().ThrowAsync<MissingRequiredServiceException>())
            .Which.MissingServices.Should()
            .ContainSingle(missing => missing.ServiceType == typeof(ICurrentTenant));
    }

    private static ServiceProvider _Build()
    {
        var services = new ServiceCollection();
        _AddTenantSource(services);
        services.AddHeadlessCaching(setup => setup.UseInMemory());
        services.AddTenantScopedCache<UserProfile>();

        return services.BuildServiceProvider();
    }

    /// <summary>The AsyncLocal-backed tenant a host's tenancy seam would register.</summary>
    private static void _AddTenantSource(IServiceCollection services)
    {
        services.AddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
    }

    private static (ICache<UserProfile> Cache, ICurrentTenant Tenant) _Resolve(IServiceProvider provider)
    {
        return (provider.GetRequiredService<ICache<UserProfile>>(), provider.GetRequiredService<ICurrentTenant>());
    }

    public sealed record UserProfile(string Name);
}
