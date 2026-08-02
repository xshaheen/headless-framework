// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Features.Models;
using Headless.Features.ValueProviders;
using Headless.Features.Values;
using Headless.Testing.Tests;

namespace Tests.ValueProviders;

public sealed class TenantFeatureValueProviderTests : TestBase
{
    private const string _AmbientTenantId = "tenant-a";
    private const string _OtherTenantId = "tenant-b";

    private readonly FakeFeatureValueStore _store = new();
    private readonly ICurrentTenant _currentTenant = Substitute.For<ICurrentTenant>();
    private readonly TenantFeatureValueProvider _sut;

    public TenantFeatureValueProviderTests()
    {
        _currentTenant.Id.Returns(_AmbientTenantId);
        _sut = new TenantFeatureValueProvider(_store, _currentTenant);
    }

    [Fact]
    public async Task should_read_explicit_provider_key_instead_of_ambient_tenant()
    {
        // given — both tenants have a value for the same feature
        var feature = new FeatureDefinition("Reporting.Enabled");
        _store.Seed(feature.Name, _sut.Name, _AmbientTenantId, "false");
        _store.Seed(feature.Name, _sut.Name, _OtherTenantId, "true");

        // when
        var result = await _sut.GetOrDefaultAsync(feature, _OtherTenantId, AbortToken);

        // then
        result.Should().Be("true");
    }

    [Fact]
    public async Task should_read_ambient_tenant_when_no_key_supplied()
    {
        // given
        var feature = new FeatureDefinition("Reporting.Enabled");
        _store.Seed(feature.Name, _sut.Name, _AmbientTenantId, "false");
        _store.Seed(feature.Name, _sut.Name, _OtherTenantId, "true");

        // when
        var result = await _sut.GetOrDefaultAsync(feature, providerKey: null, AbortToken);

        // then
        result.Should().Be("false");
    }

    [Fact]
    public async Task should_round_trip_value_written_for_another_tenant()
    {
        // given
        var feature = new FeatureDefinition("Reporting.Enabled");

        // when
        await _sut.SetAsync(feature, "true", _OtherTenantId, AbortToken);

        // then — the write lands under the requested tenant, not the ambient one
        var forOtherTenant = await _sut.GetOrDefaultAsync(feature, _OtherTenantId, AbortToken);
        var forAmbientTenant = await _sut.GetOrDefaultAsync(feature, providerKey: null, AbortToken);
        forOtherTenant.Should().Be("true");
        forAmbientTenant.Should().BeNull();
    }

    [Fact]
    public async Task should_write_to_ambient_tenant_when_no_key_supplied()
    {
        // given
        var feature = new FeatureDefinition("Reporting.Enabled");

        // when
        await _sut.SetAsync(feature, "true", providerKey: null, AbortToken);

        // then — no orphaned NULL-key row is persisted
        _store.Keys.Should().ContainSingle().Which.ProviderKey.Should().Be(_AmbientTenantId);
        var result = await _sut.GetOrDefaultAsync(feature, providerKey: null, AbortToken);
        result.Should().Be("true");
    }

    [Fact]
    public async Task should_clear_value_of_the_requested_tenant()
    {
        // given
        var feature = new FeatureDefinition("Reporting.Enabled");
        _store.Seed(feature.Name, _sut.Name, _AmbientTenantId, "false");
        _store.Seed(feature.Name, _sut.Name, _OtherTenantId, "true");

        // when
        await _sut.ClearAsync(feature, _OtherTenantId, AbortToken);

        // then
        var forOtherTenant = await _sut.GetOrDefaultAsync(feature, _OtherTenantId, AbortToken);
        var forAmbientTenant = await _sut.GetOrDefaultAsync(feature, providerKey: null, AbortToken);
        forOtherTenant.Should().BeNull();
        forAmbientTenant.Should().Be("false");
    }

    private sealed class FakeFeatureValueStore : IFeatureValueStore
    {
        private readonly Dictionary<(string Name, string ProviderName, string? ProviderKey), string?> _values = [];

        public IReadOnlyCollection<(string Name, string ProviderName, string? ProviderKey)> Keys => _values.Keys;

        public void Seed(string name, string providerName, string? providerKey, string value)
        {
            _values[(name, providerName, providerKey)] = value;
        }

        public Task<string?> GetOrDefaultAsync(
            string name,
            string providerName,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(_values.GetValueOrDefault((name, providerName, providerKey)));
        }

        public Task SetAsync(
            string name,
            string value,
            string providerName,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            _values[(name, providerName, providerKey)] = value;

            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string name,
            string providerName,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            _values.Remove((name, providerName, providerKey));

            return Task.CompletedTask;
        }
    }
}
