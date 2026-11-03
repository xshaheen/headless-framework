// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Tests.TestSetup;

namespace Tests;

/// <summary>
/// Grants for one role name in two tenants, through the EF store on a plain <see cref="Microsoft.EntityFrameworkCore.DbContext" />
/// (no tenant query filter) with the Redis grant cache: each tenant reads, grants, and revokes only its own rows.
/// </summary>
public sealed class PermissionsTenantIsolationTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private const string _TenantA = "tenant-a";
    private const string _TenantB = "tenant-b";
    private const string _ProviderName = "R";
    private const string _ProviderKey = "admin";
    private const string _Permission = "Orders.Approve";
    private const string _OtherPermission = "Orders.Cancel";

    private string? _tenantId;

    [Fact]
    public async Task should_not_grant_one_tenants_role_a_permission_another_tenant_granted_to_its_role()
    {
        // given
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();

        _tenantId = _TenantB;
        await store.GrantAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // when
        _tenantId = _TenantA;
        var tenantA = await store.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // then
        tenantA.Should().Be(PermissionGrantStatus.Undefined);
        _tenantId = _TenantB;
        (await store.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Granted);
    }

    [Fact]
    public async Task should_keep_each_tenants_grant_when_both_tenants_grant_one_permission_to_one_role()
    {
        // given both tenants grant, and the cache is cold so the checks load from the database
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();

        _tenantId = _TenantA;
        await store.GrantAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        _tenantId = _TenantB;
        await store.GrantAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        var grantCache = host.Services.GetRequiredService<ICache<PermissionGrantCacheItem>>();
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(_Permission, _ProviderName, _ProviderKey);
        _tenantId = _TenantA;
        await grantCache.RemoveAsync(cacheKey, AbortToken);
        _tenantId = _TenantB;
        await grantCache.RemoveAsync(cacheKey, AbortToken);

        // when
        _tenantId = _TenantA;
        var tenantA = await store.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        _tenantId = null;
        var hostTenant = await store.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // then
        tenantA.Should().Be(PermissionGrantStatus.Granted);
        hostTenant.Should().Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_revoke_and_regrant_in_one_tenant_without_touching_the_other_tenants_grant()
    {
        // given
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();

        _tenantId = _TenantB;
        await store.GrantAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // when tenant A revokes, then grants, its own admin role
        _tenantId = _TenantA;
        await store.RevokeAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        var afterRevoke = await store.GetAllGrantsAsync(_ProviderName, _ProviderKey, AbortToken);
        await store.GrantAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        var afterGrant = await store.GetAllGrantsAsync(_ProviderName, _ProviderKey, AbortToken);

        // then
        afterRevoke.Should().ContainSingle().Which.IsGranted.Should().BeFalse();
        afterGrant.Should().ContainSingle().Which.IsGranted.Should().BeTrue();
        _tenantId = _TenantB;
        await store.RefreshAsync(_ProviderName, _ProviderKey, AbortToken);
        (await store.GetAllGrantsAsync(_ProviderName, _ProviderKey, AbortToken))
            .Should()
            .ContainSingle()
            .Which.IsGranted.Should()
            .BeTrue();
        (await store.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Granted);
    }

    [Fact]
    public async Task should_delete_only_this_tenants_grants_and_report_them_undefined()
    {
        // given both tenants batch-grant the same names to the same role
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();
        string[] names = [_Permission, _OtherPermission];

        _tenantId = _TenantA;
        await store.GrantAsync(names, _ProviderName, _ProviderKey, AbortToken);
        _tenantId = _TenantB;
        await store.GrantAsync(names, _ProviderName, _ProviderKey, AbortToken);

        // when tenant B deletes its role's grants
        var deleted = await store.DeleteAsync(_ProviderName, _ProviderKey, AbortToken);

        // then
        deleted.Should().BeEquivalentTo(names);
        (await store.IsGrantedAsync(names, _ProviderName, _ProviderKey, AbortToken))
            .Values.Should()
            .AllSatisfy(x => x.Should().Be(PermissionGrantStatus.Undefined));
        _tenantId = _TenantA;
        await store.RefreshAsync(_ProviderName, _ProviderKey, AbortToken);
        (await store.IsGrantedAsync(names, _ProviderName, _ProviderKey, AbortToken))
            .Values.Should()
            .AllSatisfy(x => x.Should().Be(PermissionGrantStatus.Granted));
    }

    private IHost _CreateHost()
    {
        var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<DefinitionProvider>());
        host.Services.GetRequiredService<ICurrentTenant>().Id.Returns(_ => _tenantId);

        return host;
    }

    private sealed class DefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup("Orders");
            group.AddChild(_Permission);
            group.AddChild(_OtherPermission);
        }
    }
}
