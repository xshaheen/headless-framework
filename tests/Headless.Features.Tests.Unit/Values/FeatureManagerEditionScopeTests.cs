// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless;
using Headless.Caching;
using Headless.Context;
using Headless.Features;
using Headless.Features.Resources;
using Headless.MultiTenancy;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Fakes;

namespace Tests.Values;

/// <summary>
/// The Edition fallback resolves the edition of the tenant being read or written, not the caller's: a host
/// administrator acting for a tenant, a user of one tenant acting for another, and a job that restored only the
/// tenant all see that tenant's edition (through <see cref="ITenantEditionResolver"/>), or keep the written value
/// when no source can tell it.
/// </summary>
public sealed class FeatureManagerEditionScopeTests : TestBase
{
    private const string _Feature = "MaxUsers";
    private const string _Tenant = "acme";

    private readonly FakeFeatureValueStore _store = new();
    private readonly ICurrentPrincipalAccessor _principalAccessor = Substitute.For<ICurrentPrincipalAccessor>();
    private readonly CurrentTenant _currentTenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly ITenantEditionResolver _resolver = Substitute.For<ITenantEditionResolver>();
    private readonly FeatureDefinition _definition = new(_Feature, defaultValue: "10");

    public FeatureManagerEditionScopeTests()
    {
        // acme subscribes to "pro", which raises the default of 10 to 50; "basic" keeps 10.
        _store.Seed(_Feature, EditionFeatureValueProvider.ProviderName, "pro", "50");
        _store.Seed(_Feature, EditionFeatureValueProvider.ProviderName, "basic", "10");
        _resolver.GetEditionIdAsync(_Tenant, Arg.Any<CancellationToken>()).Returns("pro");
    }

    [Fact]
    public async Task should_keep_a_host_write_for_a_tenant_whose_edition_is_unknown()
    {
        // given a host administrator with no tenant and no edition claim, and no edition resolver
        _principalAccessor.Principal.Returns(_Principal());
        var sut = _CreateManager(editionResolver: null);

        // when the administrator sets acme back to 10, which only equals the host's fallback (the default)
        await sut.SetAsync(
            _Feature,
            "10",
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );

        // then the value is stored: acme inherits 50 from its edition, so dropping it would be a silent no-op
        _store.Find(_Feature, TenantFeatureValueProvider.ProviderName, _Tenant).Should().Be("10");
    }

    [Fact]
    public async Task should_not_compare_another_tenants_write_against_the_callers_edition()
    {
        // given a user of tenant-b on "basic" (10), and no edition resolver
        _principalAccessor.Principal.Returns(_Principal(tenantId: "tenant-b", editionId: "basic"));
        using var ambient = _currentTenant.Change("tenant-b");
        var sut = _CreateManager(editionResolver: null);

        // when that user sets acme's value to 10
        await sut.SetAsync(
            _Feature,
            "10",
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );

        // then the value is stored rather than cleared against tenant-b's edition
        _store.Find(_Feature, TenantFeatureValueProvider.ProviderName, _Tenant).Should().Be("10");
    }

    [Fact]
    public async Task should_compare_a_host_write_against_the_tenants_resolved_edition()
    {
        // given a host administrator and a resolver that knows acme is on "pro"
        _principalAccessor.Principal.Returns(_Principal());
        var sut = _CreateManager(_resolver);

        // when the administrator sets acme to 10 (a real downgrade) and then to 50 (what "pro" already gives)
        await sut.SetAsync(
            _Feature,
            "10",
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );
        var downgraded = _store.Find(_Feature, TenantFeatureValueProvider.ProviderName, _Tenant);
        await sut.SetAsync(
            _Feature,
            "50",
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );

        // then the downgrade is stored, and the value equal to the edition's is cleared so acme inherits it
        downgraded.Should().Be("10");
        _store.Find(_Feature, TenantFeatureValueProvider.ProviderName, _Tenant).Should().BeNull();
    }

    [Fact]
    public async Task should_clear_a_tenant_users_own_write_that_equals_its_edition_value()
    {
        // given a user of acme carrying the "pro" claim, in acme's request
        _principalAccessor.Principal.Returns(_Principal(tenantId: _Tenant, editionId: "pro"));
        using var ambient = _currentTenant.Change(_Tenant);
        var sut = _CreateManager(editionResolver: null);

        // when the user writes the value its edition already supplies
        await sut.SetAsync(
            _Feature,
            "50",
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );

        // then the claim answers the fallback and the value is cleared, as before
        _store.Find(_Feature, TenantFeatureValueProvider.ProviderName, _Tenant).Should().BeNull();
    }

    [Fact]
    public async Task should_resolve_the_edition_of_a_restored_tenant_without_a_principal()
    {
        // given a job that restored acme but no principal
        _principalAccessor.Principal.Returns((ClaimsPrincipal?)null);
        using var ambient = _currentTenant.Change(_Tenant);
        var sut = _CreateManager(_resolver);

        // when
        var value = await sut.GetAsync(_Feature, cancellationToken: AbortToken);

        // then acme's edition value applies, as it does in acme's HTTP requests
        value.Value.Should().Be("50");
        value.Provider!.Name.Should().Be(EditionFeatureValueProvider.ProviderName);
    }

    [Fact]
    public async Task should_read_a_tenants_fallback_through_its_edition_from_the_host()
    {
        // given a host administrator and a resolver that knows acme is on "pro"
        _principalAccessor.Principal.Returns(_Principal());
        var sut = _CreateManager(_resolver);

        // when
        var single = await sut.GetAsync(
            _Feature,
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );
        var all = await sut.GetAllAsync(
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );

        // then
        single.Value.Should().Be("50");
        all.Should().ContainSingle().Which.Value.Should().Be("50");
        _currentTenant.Id.Should().BeNull();
    }

    [Fact]
    public async Task should_not_read_the_callers_edition_claim_for_another_tenant()
    {
        // given a user of tenant-b on "pro", and no edition resolver
        _principalAccessor.Principal.Returns(_Principal(tenantId: "tenant-b", editionId: "pro"));
        using var ambient = _currentTenant.Change("tenant-b");
        var sut = _CreateManager(editionResolver: null);

        // when that user reads acme's value with fallback
        var value = await sut.GetAsync(
            _Feature,
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );

        // then tenant-b's edition is not reported as acme's
        value.Value.Should().Be("10");
        value.Provider!.Name.Should().Be(DefaultValueFeatureValueProvider.ProviderName);
    }

    [Fact]
    public async Task should_resolve_the_tenants_edition_through_the_cached_store_from_the_host()
    {
        // given a real cached store, where the host's own read already cached the Edition entry it resolves (none)
        _principalAccessor.Principal.Returns(_Principal());
        using var cache = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());
        var repository = Substitute.For<IFeatureValueRecordRepository>();
        repository
            .GetListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), EditionFeatureValueProvider.ProviderName, StringComparison.Ordinal)
                && string.Equals(call.ArgAt<string?>(1), "pro", StringComparison.Ordinal)
                    ?
                    [
                        new FeatureValueRecord(
                            Guid.NewGuid(),
                            _Feature,
                            "50",
                            EditionFeatureValueProvider.ProviderName,
                            "pro"
                        ),
                    ]
                    : []
            );
        var definitions = Substitute.For<IFeatureDefinitionManager>();
        definitions.FindAsync(_Feature, Arg.Any<CancellationToken>()).Returns(_definition);
        definitions.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns([_definition]);
        var store = new FeatureValueStore(
            definitions,
            repository,
            Substitute.For<IGuidGenerator>(),
            cache,
            NullLogger<FeatureValueStore>.Instance
        );
        var sut = _CreateManager(_resolver, store, definitions);
        (await sut.GetAsync(_Feature, cancellationToken: AbortToken)).Value.Should().Be("10");

        // when the administrator reads acme with fallback, twice so the second read is served from the cache
        var cold = await sut.GetAsync(
            _Feature,
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );
        var warm = await sut.GetAsync(
            _Feature,
            TenantFeatureValueProvider.ProviderName,
            _Tenant,
            cancellationToken: AbortToken
        );

        // then both resolve acme's edition, not the host's cached empty edition entry
        cold.Value.Should().Be("50");
        warm.Value.Should().Be("50");
    }

    private FeatureManager _CreateManager(
        ITenantEditionResolver? editionResolver,
        IFeatureValueStore? store = null,
        IFeatureDefinitionManager? definitionManager = null
    )
    {
        store ??= _store;
        var definitions = definitionManager ?? Substitute.For<IFeatureDefinitionManager>();
        definitions.FindAsync(_Feature, Arg.Any<CancellationToken>()).Returns(_definition);
        definitions.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns([_definition]);

        var providers = Substitute.For<IFeatureValueProviderManager>();
        providers.ValueProviders.Returns([
            new TenantFeatureValueProvider(store, _currentTenant),
            new EditionFeatureValueProvider(store, _principalAccessor, _currentTenant, editionResolver),
            new DefaultValueFeatureValueProvider(),
        ]);

        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns("host");

        return new FeatureManager(
            definitions,
            providers,
            new DefaultFeatureErrorsDescriptor(),
            hostIdentity,
            _currentTenant
        );
    }

    private static ClaimsPrincipal _Principal(string? tenantId = null, string? editionId = null)
    {
        List<Claim> claims = [new(UserClaimTypes.UserId, Guid.NewGuid().ToString())];

        if (tenantId is not null)
        {
            claims.Add(new Claim(UserClaimTypes.TenantId, tenantId));
        }

        if (editionId is not null)
        {
            claims.Add(new Claim(UserClaimTypes.EditionId, editionId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
