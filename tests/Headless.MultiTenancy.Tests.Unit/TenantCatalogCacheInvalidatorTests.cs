// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class TenantCatalogCacheInvalidatorTests : TestBase
{
    private readonly ITenantStore _store = Substitute.For<ITenantStore>();
    private readonly TenantCatalogOptions _options = new();
    private readonly InMemoryCache _backingCache = new(TimeProvider.System, new InMemoryCacheOptions());
    private readonly TenantCatalogService _service;
    private readonly TenantCatalogCacheInvalidator _sut;

    public TenantCatalogCacheInvalidatorTests()
    {
        var identifierCache = new Cache<TenantIdentifierCacheItem>(_backingCache);
        var infoCache = new Cache<TenantInfoCacheItem>(_backingCache);

        _service = new TenantCatalogService(
            _store,
            identifierCache,
            infoCache,
            Options.Create(_options),
            new TenantCatalogIgnoredIdentifierSet(Options.Create(_options)),
            NullLogger<TenantCatalogService>.Instance
        );

        _sut = new TenantCatalogCacheInvalidator(identifierCache, infoCache);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _backingCache.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_make_the_next_resolution_reread_the_store_after_a_re_pointed_identifier_is_invalidated()
    {
        // given — the identifier is cached against the first tenant, then re-pointed in the store
        _store
            .FindByIdentifierAsync("acme", Arg.Any<CancellationToken>())
            .Returns(
                new TenantInfo("ten_1", "acme", "Acme", isEnabled: true),
                new TenantInfo("ten_2", "acme", "Acme Two", isEnabled: true)
            );
        (await _service.ResolveAsync("acme", AbortToken)).Tenant!.Id.Should().Be("ten_1");

        // when — invalidated with the raw, un-normalized form an operator would type
        await _sut.InvalidateIdentifierAsync(" ACME ", AbortToken);
        var outcome = await _service.ResolveAsync("acme", AbortToken);

        // then
        outcome.Tenant!.Id.Should().Be("ten_2");
        await _store.Received(2).FindByIdentifierAsync("acme", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_serving_the_cached_mapping_when_nothing_is_invalidated()
    {
        // given — the baseline the invalidation tests contrast with
        _store
            .FindByIdentifierAsync("acme", Arg.Any<CancellationToken>())
            .Returns(
                new TenantInfo("ten_1", "acme", "Acme", isEnabled: true),
                new TenantInfo("ten_2", "acme", "Acme Two", isEnabled: true)
            );
        await _service.ResolveAsync("acme", AbortToken);

        // when
        var outcome = await _service.ResolveAsync("acme", AbortToken);

        // then
        outcome.Tenant!.Id.Should().Be("ten_1");
        await _store.Received(1).FindByIdentifierAsync("acme", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_make_the_next_id_lookup_reread_the_store_after_the_tenant_is_invalidated()
    {
        // given — the tenant is cached enabled, then disabled in the store
        _store
            .FindByIdAsync("ten_1", Arg.Any<CancellationToken>())
            .Returns(
                new TenantInfo("ten_1", "acme", "Acme", isEnabled: true),
                new TenantInfo("ten_1", "acme", "Acme", isEnabled: false)
            );
        (await _service.FindByIdAsync("ten_1", AbortToken))!.IsEnabled.Should().BeTrue();

        // when
        await _sut.InvalidateTenantAsync("ten_1", AbortToken);
        var tenant = await _service.FindByIdAsync("ten_1", AbortToken);

        // then
        tenant!.IsEnabled.Should().BeFalse();
        await _store.Received(2).FindByIdAsync("ten_1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_let_a_disable_reach_identifier_resolution_once_the_tenant_is_invalidated()
    {
        // given — identifier resolution fills the id axis; the mapping itself stays valid after a disable
        _store
            .FindByIdentifierAsync("acme", Arg.Any<CancellationToken>())
            .Returns(new TenantInfo("ten_1", "acme", "Acme", isEnabled: true));
        _store
            .FindByIdAsync("ten_1", Arg.Any<CancellationToken>())
            .Returns(new TenantInfo("ten_1", "acme", "Acme", isEnabled: false));
        (await _service.ResolveAsync("acme", AbortToken)).Kind.Should().Be(TenantResolutionKind.Resolved);

        // when
        await _sut.InvalidateTenantAsync("ten_1", AbortToken);
        var outcome = await _service.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Disabled);
    }

    [Fact]
    public async Task should_make_a_newly_created_tenant_resolvable_before_the_negative_entry_expires()
    {
        // given — the identifier was probed while unknown, so a negative entry is cached
        _store
            .FindByIdentifierAsync("acme", Arg.Any<CancellationToken>())
            .Returns(null, new TenantInfo("ten_1", "acme", "Acme", isEnabled: true));
        (await _service.ResolveAsync("acme", AbortToken)).Kind.Should().Be(TenantResolutionKind.Unknown);

        // when
        await _sut.InvalidateIdentifierAsync("acme", AbortToken);
        var outcome = await _service.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
    }

    [Fact]
    public async Task should_route_both_identifiers_to_their_new_state_after_a_rename_is_invalidated()
    {
        // given — ten_1 was "acme" and "acme-new" was probed while unknown; the store then renames ten_1
        var renamed = false;
        _store
            .FindByIdentifierAsync("acme", Arg.Any<CancellationToken>())
            .Returns(_ => renamed ? null : new TenantInfo("ten_1", "acme", "Acme", isEnabled: true));
        _store
            .FindByIdentifierAsync("acme-new", Arg.Any<CancellationToken>())
            .Returns(_ => renamed ? new TenantInfo("ten_1", "acme-new", "Acme", isEnabled: true) : null);
        _store
            .FindByIdAsync("ten_1", Arg.Any<CancellationToken>())
            .Returns(_ => new TenantInfo("ten_1", renamed ? "acme-new" : "acme", "Acme", isEnabled: true));

        await _service.ResolveAsync("acme", AbortToken);
        await _service.ResolveAsync("acme-new", AbortToken);
        renamed = true;

        // when
        await _sut.InvalidateTenantAsync("ten_1", AbortToken);
        await _sut.InvalidateIdentifierAsync("acme", AbortToken);
        await _sut.InvalidateIdentifierAsync("acme-new", AbortToken);

        // then
        (await _service.ResolveAsync("acme", AbortToken))
            .Kind.Should()
            .Be(TenantResolutionKind.Unknown);
        var resolved = await _service.ResolveAsync("acme-new", AbortToken);
        resolved.Tenant!.Id.Should().Be("ten_1");
        (await _service.FindByIdAsync("ten_1", AbortToken))!.Identifier.Should().Be("acme-new");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_throw_when_the_identifier_is_empty_or_white_space(string identifier)
    {
        // when
        var act = () => _sut.InvalidateIdentifierAsync(identifier, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_the_identifier_is_null()
    {
        // when
        var act = () => _sut.InvalidateIdentifierAsync(null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_throw_when_the_tenant_id_is_empty_or_white_space(string id)
    {
        // when
        var act = () => _sut.InvalidateTenantAsync(id, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_propagate_a_cache_fault_so_the_caller_knows_the_entry_may_still_be_cached()
    {
        // given — unlike the read path, which degrades a cache fault to a miss, an invalidation that did not
        // happen must not look like one that did
        var identifierCache = Substitute.For<ICache<TenantIdentifierCacheItem>>();
        var infoCache = Substitute.For<ICache<TenantInfoCacheItem>>();
        identifierCache
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache down"));
        infoCache
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache down"));
        var sut = new TenantCatalogCacheInvalidator(identifierCache, infoCache);

        // when
        var invalidateIdentifier = () => sut.InvalidateIdentifierAsync("acme", AbortToken);
        var invalidateTenant = () => sut.InvalidateTenantAsync("ten_1", AbortToken);

        // then
        await invalidateIdentifier.Should().ThrowAsync<InvalidOperationException>().WithMessage("cache down");
        await invalidateTenant.Should().ThrowAsync<InvalidOperationException>().WithMessage("cache down");
    }

    [Fact]
    public async Task should_remove_only_the_exact_key_even_when_the_identifier_carries_glob_characters()
    {
        // given — a custom IdentifierPattern may admit '*', '?', or '['; an exact-key removal cannot widen
        var identifierCache = Substitute.For<ICache<TenantIdentifierCacheItem>>();
        var sut = new TenantCatalogCacheInvalidator(identifierCache, Substitute.For<ICache<TenantInfoCacheItem>>());

        // when
        await sut.InvalidateIdentifierAsync("a*[b]?", AbortToken);

        // then
        await identifierCache.Received(1).RemoveAsync("tenancy:catalog:identifier:a*[b]?", AbortToken);
        await identifierCache.DidNotReceiveWithAnyArgs().RemoveByPrefixAsync(default!, default);
    }
}
