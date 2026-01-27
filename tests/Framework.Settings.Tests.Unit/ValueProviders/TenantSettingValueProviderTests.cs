// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Settings.Models;
using Framework.Settings.ValueProviders;
using Framework.Settings.Values;
using Framework.Testing.Tests;
using NSubstitute;

namespace Tests.ValueProviders;

public sealed class TenantSettingValueProviderTests : TestBase
{
    private readonly ISettingValueStore _store = Substitute.For<ISettingValueStore>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly TenantSettingValueProvider _sut;

    public TenantSettingValueProviderTests()
    {
        _sut = new TenantSettingValueProvider(_store, _tenant);
    }

    [Fact]
    public async Task should_read_from_store_with_tenant_key()
    {
        // given
        var setting = new SettingDefinition("tenant.setting");
        _tenant.Id.Returns("tenant-123");
        _store
            .GetOrDefaultAsync("tenant.setting", SettingValueProviderNames.Tenant, "tenant-123", AbortToken)
            .Returns("tenant-value");

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().Be("tenant-value");
    }

    [Fact]
    public async Task should_use_current_tenant_id()
    {
        // given
        var setting = new SettingDefinition("tenant.setting");
        _tenant.Id.Returns("current-tenant-id");

        // when
        await _sut.GetOrDefaultAsync(setting, providerKey: null, AbortToken);

        // then
        await _store
            .Received(1)
            .GetOrDefaultAsync("tenant.setting", SettingValueProviderNames.Tenant, "current-tenant-id", AbortToken);
    }

    [Fact]
    public async Task should_use_provided_key_over_current()
    {
        // given
        var setting = new SettingDefinition("tenant.setting");
        _tenant.Id.Returns("current-tenant-id");

        // when
        await _sut.GetOrDefaultAsync(setting, providerKey: "explicit-tenant-id", AbortToken);

        // then
        await _store
            .Received(1)
            .GetOrDefaultAsync("tenant.setting", SettingValueProviderNames.Tenant, "explicit-tenant-id", AbortToken);
    }

    [Fact]
    public async Task should_return_null_when_no_tenant()
    {
        // given
        var setting = new SettingDefinition("tenant.setting");
        _tenant.Id.Returns((string?)null);
        _store
            .GetOrDefaultAsync("tenant.setting", SettingValueProviderNames.Tenant, null, AbortToken)
            .Returns((string?)null);

        // when
        var result = await _sut.GetOrDefaultAsync(setting, cancellationToken: AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_write_to_store_with_tenant_key()
    {
        // given
        var setting = new SettingDefinition("tenant.setting");
        _tenant.Id.Returns("tenant-456");

        // when
        await _sut.SetAsync(setting, "new-tenant-value", providerKey: null, AbortToken);

        // then
        await _store
            .Received(1)
            .SetAsync("tenant.setting", "new-tenant-value", SettingValueProviderNames.Tenant, "tenant-456", AbortToken);
    }

    [Fact]
    public async Task should_clear_from_store()
    {
        // given
        var setting = new SettingDefinition("tenant.setting");
        _tenant.Id.Returns("tenant-789");

        // when
        await _sut.ClearAsync(setting, providerKey: null, AbortToken);

        // then
        await _store
            .Received(1)
            .DeleteAsync("tenant.setting", SettingValueProviderNames.Tenant, "tenant-789", AbortToken);
    }

    [Fact]
    public void should_return_provider_name()
    {
        // when & then
        _sut.Name.Should().Be(SettingValueProviderNames.Tenant);
        TenantSettingValueProvider.ProviderName.Should().Be("Tenant");
    }
}
