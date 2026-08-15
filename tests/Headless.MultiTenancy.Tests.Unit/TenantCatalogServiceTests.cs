// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class TenantCatalogServiceTests : TestBase
{
    private readonly ITenantStore _store = Substitute.For<ITenantStore>();
    private readonly ICache<TenantIdentifierCacheItem> _identifierCache = Substitute.For<
        ICache<TenantIdentifierCacheItem>
    >();
    private readonly ICache<TenantInfoCacheItem> _infoCache = Substitute.For<ICache<TenantInfoCacheItem>>();
    private readonly TenantCatalogOptions _options = new();
    private readonly TenantCatalogService _sut;

    public TenantCatalogServiceTests()
    {
        _sut = new TenantCatalogService(
            _store,
            _identifierCache,
            _infoCache,
            Options.Create(_options),
            NullLogger<TenantCatalogService>.Instance
        );
    }

    #region Resolution outcomes (AE1-AE4, AE7, AE11)

    [Fact]
    public async Task should_resolve_identifier_to_canonical_tenant()
    {
        // given — AE1
        var tenant = new TenantInfo("ten_123", "acme", "Acme", isEnabled: true);
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);

        // when
        var outcome = await _sut.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task should_return_unknown_when_store_has_no_match()
    {
        // given — AE2
        _store.FindByIdentifierAsync("ghost", AbortToken).Returns((TenantInfo?)null);

        // when
        var outcome = await _sut.ResolveAsync("ghost", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Unknown);
        outcome.Tenant.Should().BeNull();
    }

    [Fact]
    public async Task should_return_disabled_when_tenant_is_disabled()
    {
        // given — AE3
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: false);
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);

        // when
        var outcome = await _sut.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Disabled);
        outcome.Tenant.Should().BeNull();
    }

    [Fact]
    public async Task should_return_ignored_without_calling_the_store()
    {
        // given — AE4
        _options.IgnoredIdentifiers.Add("www");
        _store
            .FindByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store must not be called for an ignored identifier"));

        // when
        var outcome = await _sut.ResolveAsync("www", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Ignored);
    }

    [Fact]
    public async Task should_match_ignored_identifiers_case_insensitively()
    {
        // given — the ignored-list entry itself may carry mixed case (options doc)
        _options.IgnoredIdentifiers.Add("WWW");
        _store
            .FindByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store must not be called for an ignored identifier"));

        // when
        var outcome = await _sut.ResolveAsync("www", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Ignored);
    }

    [Fact]
    public async Task should_normalize_identifier_before_lookup()
    {
        // given — AE7
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);

        // when
        var outcome = await _sut.ResolveAsync(" ACME ", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        await _store.Received(1).FindByIdentifierAsync("acme", AbortToken);
    }

    [Fact]
    public async Task should_return_invalid_when_identifier_exceeds_max_length_without_cache_or_store_calls()
    {
        // given — AE11
        var tooLong = new string('a', _options.MaxIdentifierLength + 1);

        // when
        var outcome = await _sut.ResolveAsync(tooLong, AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Invalid);
        await _store.DidNotReceive().FindByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _identifierCache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bad_identifier")] // underscore is outside the default slug shape
    [InlineData("   ")] // empty after trim
    public async Task should_return_invalid_for_bad_shape_without_cache_or_store_calls(string identifier)
    {
        // given — AE11
        // when
        var outcome = await _sut.ResolveAsync(identifier, AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Invalid);
        await _store.DidNotReceive().FindByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _identifierCache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_throw_when_identifier_is_null()
    {
        // when
        var act = () => _sut.ResolveAsync(null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region Staleness bound (AE8)

    [Fact]
    public async Task should_keep_serving_the_cached_enabled_result_until_the_entry_expires_then_reflect_disable()
    {
        // given — AE8: model expiration as cache-hit(stale)->miss(expired) transitions, no real sleeps.
        var enabledSnapshot = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        var disabledTenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: false);

        _store.FindByIdentifierAsync("acme", AbortToken).Returns(disabledTenant);
        _store.FindByIdAsync("ten_1", AbortToken).Returns(disabledTenant);

        _identifierCache
            .GetAsync(Arg.Any<string>(), AbortToken)
            .Returns(new CacheValue<TenantIdentifierCacheItem>(new TenantIdentifierCacheItem("ten_1"), hasValue: true));
        _infoCache
            .GetAsync(Arg.Any<string>(), AbortToken)
            .Returns(new CacheValue<TenantInfoCacheItem>(new TenantInfoCacheItem(enabledSnapshot), hasValue: true));

        // when — still within the cache window
        var stillCached = await _sut.ResolveAsync("acme", AbortToken);

        // then — stale cached data still wins; the store's disable has not propagated yet
        stillCached.Kind.Should().Be(TenantResolutionKind.Resolved);

        // when — simulate expiry: both cache axes now miss
        _identifierCache
            .GetAsync(Arg.Any<string>(), AbortToken)
            .Returns(CacheValue<TenantIdentifierCacheItem>.NoValue);
        _infoCache.GetAsync(Arg.Any<string>(), AbortToken).Returns(CacheValue<TenantInfoCacheItem>.NoValue);

        var afterExpiry = await _sut.ResolveAsync("acme", AbortToken);

        // then — the store's disable is now observed
        afterExpiry.Kind.Should().Be(TenantResolutionKind.Disabled);
    }

    #endregion

    #region Store fault classification (KTD4)

    [Fact]
    public async Task should_propagate_store_exception_unwrapped_never_mapping_to_unknown()
    {
        // given
        _store
            .FindByIdentifierAsync("acme", AbortToken)
            .Returns(Task.FromException<TenantInfo?>(new InvalidOperationException("store down")));

        // when
        var act = () => _sut.ResolveAsync("acme", AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("store down");
    }

    #endregion

    #region Cache fault degradation (KTD4)

    [Fact]
    public async Task should_fall_through_to_store_when_identifier_cache_read_faults()
    {
        // given
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _identifierCache
            .GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache down"));
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);

        // when
        var outcome = await _sut.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task should_fall_through_to_store_when_info_cache_read_faults()
    {
        // given — identifier cache hits (positive), but the info-axis read faults
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _identifierCache
            .GetAsync(Arg.Any<string>(), AbortToken)
            .Returns(new CacheValue<TenantIdentifierCacheItem>(new TenantIdentifierCacheItem("ten_1"), hasValue: true));
        _infoCache
            .GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache down"));
        _store.FindByIdAsync("ten_1", AbortToken).Returns(tenant);

        // when
        var outcome = await _sut.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task should_return_resolved_outcome_even_when_positive_cache_write_faults()
    {
        // given
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);
        _identifierCache
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<TenantIdentifierCacheItem>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("cache write failed"));
        _infoCache
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<TenantInfoCacheItem>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("cache write failed"));

        // when
        var outcome = await _sut.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task should_return_unknown_outcome_even_when_negative_cache_write_faults()
    {
        // given
        _store.FindByIdentifierAsync("ghost", AbortToken).Returns((TenantInfo?)null);
        _identifierCache
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<TenantIdentifierCacheItem>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("cache write failed"));

        // when
        var outcome = await _sut.ResolveAsync("ghost", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Unknown);
    }

    [Fact]
    public async Task should_propagate_cancellation_from_cache_read_without_falling_through_to_the_store()
    {
        // given — OperationCanceledException is never a cache fault (KTD4)
        using var cts = new CancellationTokenSource();
        _identifierCache
            .GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // when
        var act = () => _sut.ResolveAsync("acme", cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _store.DidNotReceive().FindByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Negative caching (KTD5)

    [Fact]
    public async Task should_cache_unknown_identifier_negatively_under_the_configured_expiration()
    {
        // given
        _store.FindByIdentifierAsync("ghost", AbortToken).Returns((TenantInfo?)null);

        // when
        await _sut.ResolveAsync("ghost", AbortToken);

        // then
        await _identifierCache
            .Received(1)
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Is<TenantIdentifierCacheItem>(item => item.TenantId == null),
                _options.UnknownIdentifierCacheExpiration,
                AbortToken
            );
    }

    [Fact]
    public async Task should_skip_negative_caching_when_unknown_identifier_expiration_is_zero()
    {
        // given
        _options.UnknownIdentifierCacheExpiration = TimeSpan.Zero;
        _store.FindByIdentifierAsync("ghost", AbortToken).Returns((TenantInfo?)null);

        // when
        await _sut.ResolveAsync("ghost", AbortToken);

        // then
        await _identifierCache
            .DidNotReceive()
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<TenantIdentifierCacheItem>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_hit_the_store_again_after_the_negative_entry_expires()
    {
        // given — always-miss cache simulates the negative entry having expired between calls
        _identifierCache
            .GetAsync(Arg.Any<string>(), AbortToken)
            .Returns(CacheValue<TenantIdentifierCacheItem>.NoValue);
        _store
            .FindByIdentifierAsync("acme", AbortToken)
            .Returns((TenantInfo?)null, new TenantInfo("ten_1", "acme", "Acme", isEnabled: true));

        // when
        var first = await _sut.ResolveAsync("acme", AbortToken);
        var second = await _sut.ResolveAsync("acme", AbortToken);

        // then
        first.Kind.Should().Be(TenantResolutionKind.Unknown);
        second.Kind.Should().Be(TenantResolutionKind.Resolved);
        await _store.Received(2).FindByIdentifierAsync("acme", AbortToken);
    }

    #endregion

    #region Defensive snapshot (KTD5, R9)

    [Fact]
    public async Task should_not_let_mutating_a_returned_tenant_affect_the_next_cached_read()
    {
        // given — a cache hit on the id axis always returns a base-shape clone
        var cached = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        cached.ExtraProperties["region"] = "eu";
        _infoCache
            .GetAsync(Arg.Any<string>(), AbortToken)
            .Returns(new CacheValue<TenantInfoCacheItem>(new TenantInfoCacheItem(cached), hasValue: true));

        // when
        var first = await _sut.FindByIdAsync("ten_1", AbortToken);
        first!.ExtraProperties["region"] = "mutated";
        var second = await _sut.FindByIdAsync("ten_1", AbortToken);

        // then
        second.Should().NotBeSameAs(first);
        second!.ExtraProperties["region"].Should().Be("eu");
    }

    #endregion

    #region FindByIdAsync (R9)

    [Fact]
    public async Task should_return_null_when_id_has_no_catalog_row()
    {
        // given
        _store.FindByIdAsync("ten_missing", AbortToken).Returns((TenantInfo?)null);

        // when
        var result = await _sut.FindByIdAsync("ten_missing", AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_disabled_tenant_info_without_rejecting()
    {
        // given — accessor reads never reject on disablement (R9)
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: false);
        _store.FindByIdAsync("ten_1", AbortToken).Returns(tenant);

        // when
        var result = await _sut.FindByIdAsync("ten_1", AbortToken);

        // then
        result.Should().NotBeNull();
        result!.IsEnabled.Should().BeFalse();
    }

    #endregion
}
