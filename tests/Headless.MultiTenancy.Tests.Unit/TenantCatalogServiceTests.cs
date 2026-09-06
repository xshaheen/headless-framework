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

    /// <summary>The entry the identifier factory produced, captured by the factory-backed arrangement.</summary>
    private TenantIdentifierCacheItem? _writtenIdentifierEntry;

    /// <summary>The entry options the identifier factory left on its context (adaptive expiration).</summary>
    private CacheEntryOptions? _writtenIdentifierOptions;

    public TenantCatalogServiceTests()
    {
        _sut = new TenantCatalogService(
            _store,
            _identifierCache,
            _infoCache,
            Options.Create(_options),
            new TenantCatalogIgnoredIdentifierSet(Options.Create(_options)),
            NullLogger<TenantCatalogService>.Instance
        );

        _ArrangeColdIdentifierCache();
    }

    #region Identifier-cache arrangements

    // The identifier axis reads through ICache<T>.GetOrAddAsync, whose factory-backed/single-flight behavior a
    // substitute does not implement: left unarranged it answers NoValue and never runs the factory, which would
    // silently exercise the service's cache-fault fallback instead of its normal path. The default arrangement
    // therefore mimics a cold factory-backed cache — run the factory, keep what it produced (entry + adaptive
    // options, which is where negative caching is now expressed), and hand the value back as a hit.

    /// <summary>
    /// The entry options production hands to <c>GetOrAddAsync</c>, matched as a literal value rather than with
    /// <c>Arg.Any&lt;CacheEntryOptions&gt;()</c>. <see cref="CacheEntryOptions"/> declares an explicit public
    /// parameterless constructor that sets non-zero defaults, so the value NSubstitute treats as "the default
    /// for this type" is not <c>default(CacheEntryOptions)</c> — the value a matcher actually substitutes. The
    /// specification then never binds to the argument and leaks into the next call as a
    /// <c>RedundantArgumentMatcherException</c>. A literal keeps the assertion strictly more specific anyway.
    /// </summary>
    private CacheEntryOptions _ExpectedEntryOptions => CacheEntryOptions.FromTimeSpan(_options.CacheExpiration);

    private static Func<
        CacheFactoryContext<TenantIdentifierCacheItem>,
        CancellationToken,
        ValueTask<CacheFactoryResult<TenantIdentifierCacheItem>>
    > _AnyIdentifierFactory()
    {
        return Arg.Any<
            Func<
                CacheFactoryContext<TenantIdentifierCacheItem>,
                CancellationToken,
                ValueTask<CacheFactoryResult<TenantIdentifierCacheItem>>
            >
        >();
    }

    private void _ArrangeColdIdentifierCache()
    {
        _ArrangeFactoryBackedIdentifierCache(faultAfterFactory: false);
    }

    /// <summary>Runs the factory as a cold cache would, then fails the write (KTD4's write-fault case).</summary>
    private void _ArrangeIdentifierCacheWriteFault()
    {
        _ArrangeFactoryBackedIdentifierCache(faultAfterFactory: true);
    }

    private void _ArrangeFactoryBackedIdentifierCache(bool faultAfterFactory)
    {
        _identifierCache
            .GetOrAddAsync(
                Arg.Any<string>(),
                _AnyIdentifierFactory(),
                _ExpectedEntryOptions,
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new ValueTask<CacheValue<TenantIdentifierCacheItem>>(
                _RunIdentifierFactoryAsync(
                    call.ArgAt<string>(0),
                    call.ArgAt<
                        Func<
                            CacheFactoryContext<TenantIdentifierCacheItem>,
                            CancellationToken,
                            ValueTask<CacheFactoryResult<TenantIdentifierCacheItem>>
                        >
                    >(1),
                    call.ArgAt<CacheEntryOptions>(2),
                    faultAfterFactory,
                    call.ArgAt<CancellationToken>(3)
                )
            ));

        // Arranging invokes the member; drop that call so the received-call assertions below stay exact.
        _identifierCache.ClearReceivedCalls();
    }

    private void _ArrangeIdentifierCacheHit(TenantIdentifierCacheItem entry)
    {
        _identifierCache
            .GetOrAddAsync(
                Arg.Any<string>(),
                _AnyIdentifierFactory(),
                _ExpectedEntryOptions,
                Arg.Any<CancellationToken>()
            )
            .Returns(new CacheValue<TenantIdentifierCacheItem>(entry, hasValue: true));

        _identifierCache.ClearReceivedCalls();
    }

    private async Task<CacheValue<TenantIdentifierCacheItem>> _RunIdentifierFactoryAsync(
        string key,
        Func<
            CacheFactoryContext<TenantIdentifierCacheItem>,
            CancellationToken,
            ValueTask<CacheFactoryResult<TenantIdentifierCacheItem>>
        > factory,
        CacheEntryOptions options,
        bool faultAfterFactory,
        CancellationToken cancellationToken
    )
    {
        var context = new CacheFactoryContext<TenantIdentifierCacheItem>(CacheValue<TenantIdentifierCacheItem>.NoValue)
        {
            Key = key,
            Options = options,
        };

        var result = await factory(context, cancellationToken);

        _writtenIdentifierEntry = result.Value;
        _writtenIdentifierOptions = context.Options;

        if (faultAfterFactory)
        {
            throw new InvalidOperationException("cache write failed");
        }

        return new CacheValue<TenantIdentifierCacheItem>(result.Value, hasValue: true);
    }

    #endregion

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
    public async Task should_match_ignored_identifiers_that_are_configured_with_surrounding_whitespace()
    {
        // given — the configured entry is normalized (trimmed) the same way the incoming identifier is,
        // so an accidentally padded appsettings value still takes effect rather than silently never
        // matching. Pins TenantCatalogIgnoredIdentifierSet's trim of the CONFIGURED side.
        _options.IgnoredIdentifiers.Add(" www ");
        _store
            .FindByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store must not be called for an ignored identifier"));

        // when
        var outcome = await _sut.ResolveAsync("www", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Ignored);
    }

    [Fact]
    public async Task should_match_ignored_identifiers_configured_with_both_whitespace_and_mixed_case()
    {
        // given — trimming and case-folding of the configured entry compose.
        _options.IgnoredIdentifiers.Add("\t WWW \t");
        _store
            .FindByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store must not be called for an ignored identifier"));

        // when
        var outcome = await _sut.ResolveAsync(" WwW ", AbortToken);

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
        _identifierCache.ReceivedCalls().Should().BeEmpty();
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
        _identifierCache.ReceivedCalls().Should().BeEmpty();
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

        _ArrangeIdentifierCacheHit(new TenantIdentifierCacheItem("ten_1"));
        _infoCache
            .GetAsync(Arg.Any<string>(), AbortToken)
            .Returns(new CacheValue<TenantInfoCacheItem>(new TenantInfoCacheItem(enabledSnapshot), hasValue: true));

        // when — still within the cache window
        var stillCached = await _sut.ResolveAsync("acme", AbortToken);

        // then — stale cached data still wins; the store's disable has not propagated yet
        stillCached.Kind.Should().Be(TenantResolutionKind.Resolved);

        // when — simulate expiry: both cache axes now miss, and the identifier factory re-reads the store
        _ArrangeColdIdentifierCache();
        _infoCache.GetAsync(Arg.Any<string>(), AbortToken).Returns(CacheValue<TenantInfoCacheItem>.NoValue);

        var afterExpiry = await _sut.ResolveAsync("acme", AbortToken);

        // then — the store's disable is now observed
        afterExpiry.Kind.Should().Be(TenantResolutionKind.Disabled);
    }

    #endregion

    #region Single-flight on a cold identifier

    [Fact]
    public async Task should_populate_both_axes_from_one_store_hit_on_an_identifier_miss()
    {
        // given
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);

        // when
        await _sut.ResolveAsync("acme", AbortToken);

        // then — the identifier entry maps to the canonical id under CacheExpiration…
        _writtenIdentifierEntry.Should().NotBeNull();
        _writtenIdentifierEntry!.TenantId.Should().Be("ten_1");
        _writtenIdentifierOptions!.Value.Duration.Should().Be(_options.CacheExpiration);

        // …and the id axis is written from the same store hit, inside the same factory, so neither this
        // resolution nor a later FindByIdAsync has to re-read the store.
        await _infoCache
            .Received(1)
            .UpsertAsync(
                TenantInfoCacheItem.CalculateCacheKey("ten_1"),
                Arg.Is<TenantInfoCacheItem>(item => item.TenantInfo.Id == "ten_1"),
                _options.CacheExpiration,
                AbortToken
            );

        await _store.Received(1).FindByIdentifierAsync("acme", AbortToken);
    }

    [Fact]
    public async Task should_read_the_store_once_when_concurrent_callers_resolve_the_same_cold_identifier()
    {
        // given — single-flight lives in the cache's GetOrAddAsync, so this is the one test that runs the
        // service against a real ICache<T> instead of a substitute.
        using var backingCache = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());

        var sut = new TenantCatalogService(
            _store,
            new Cache<TenantIdentifierCacheItem>(backingCache),
            new Cache<TenantInfoCacheItem>(backingCache),
            Options.Create(_options),
            new TenantCatalogIgnoredIdentifierSet(Options.Create(_options)),
            NullLogger<TenantCatalogService>.Instance
        );

        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        var storeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storeReads = 0;

        async Task<TenantInfo?> readStoreAsync()
        {
            Interlocked.Increment(ref storeReads);
            storeEntered.TrySetResult();
            await releaseStore.Task;

            return tenant;
        }

        _store.FindByIdentifierAsync("acme", Arg.Any<CancellationToken>()).Returns(_ => readStoreAsync());

        // when — all callers are started before the one in-flight store read is allowed to finish, so the
        // losers are parked inside the cache (on its per-key lock, or reading the entry the winner wrote)
        // rather than racing a timer: no ordering of the callers can make this assertion flip.
        var callers = Enumerable
            .Range(0, 16)
            .Select(_ => Task.Run(() => sut.ResolveAsync("acme", AbortToken), AbortToken))
            .ToArray();

        await storeEntered.Task.WaitAsync(AbortToken);
        releaseStore.SetResult();

        var outcomes = await Task.WhenAll(callers);

        // then
        outcomes.Should().OnlyContain(outcome => outcome.Kind == TenantResolutionKind.Resolved);
        storeReads.Should().Be(1);
    }

    #endregion

    #region Store fault classification (KTD4)

    [Fact]
    public async Task should_propagate_store_exception_unwrapped_never_mapping_to_unknown()
    {
        // given — the store now faults inside the cache factory; the fault must still surface unchanged
        // rather than being absorbed as a cache fault and degraded to an unknown tenant.
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
        // given — the read side faults before the factory runs
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _identifierCache
            .GetOrAddAsync(
                Arg.Any<string>(),
                _AnyIdentifierFactory(),
                _ExpectedEntryOptions,
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("cache down"));
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);

        // when
        var outcome = await _sut.ResolveAsync("acme", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant.Should().BeSameAs(tenant);
        await _store.Received(1).FindByIdentifierAsync("acme", AbortToken);
    }

    [Fact]
    public async Task should_fall_through_to_store_when_info_cache_read_faults()
    {
        // given — identifier cache hits (positive), but the info-axis read faults
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _ArrangeIdentifierCacheHit(new TenantIdentifierCacheItem("ten_1"));
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
        // given — the store answered and only the cache write failed
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);
        _ArrangeIdentifierCacheWriteFault();
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

        // then — the store-derived outcome stands, and the store is not consulted a second time
        outcome.Kind.Should().Be(TenantResolutionKind.Resolved);
        outcome.Tenant.Should().BeSameAs(tenant);
        await _store.Received(1).FindByIdentifierAsync("acme", AbortToken);
    }

    [Fact]
    public async Task should_return_unknown_outcome_even_when_negative_cache_write_faults()
    {
        // given
        _store.FindByIdentifierAsync("ghost", AbortToken).Returns((TenantInfo?)null);
        _ArrangeIdentifierCacheWriteFault();

        // when
        var outcome = await _sut.ResolveAsync("ghost", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Unknown);
        await _store.Received(1).FindByIdentifierAsync("ghost", AbortToken);
    }

    [Fact]
    public async Task should_return_resolved_outcome_even_when_the_id_axis_write_faults()
    {
        // given — the id-axis write happens inside the identifier factory; its failure must stay invisible
        // to the factory's result rather than aborting it as a factory (store) fault.
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);
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
        _writtenIdentifierEntry.Should().NotBeNull();
        _writtenIdentifierEntry!.TenantId.Should().Be("ten_1");
    }

    [Fact]
    public async Task should_propagate_cancellation_from_cache_read_without_falling_through_to_the_store()
    {
        // given — OperationCanceledException is never a cache fault (KTD4)
        using var cts = new CancellationTokenSource();
        _identifierCache
            .GetOrAddAsync(
                Arg.Any<string>(),
                _AnyIdentifierFactory(),
                _ExpectedEntryOptions,
                Arg.Any<CancellationToken>()
            )
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

        // then — a negative entry carries no tenant id and takes the shorter unknown-identifier window
        _writtenIdentifierEntry.Should().NotBeNull();
        _writtenIdentifierEntry!.TenantId.Should().BeNull();
        _writtenIdentifierOptions!.Value.Duration.Should().Be(_options.UnknownIdentifierCacheExpiration);
    }

    [Fact]
    public async Task should_skip_negative_caching_when_unknown_identifier_expiration_is_zero()
    {
        // given — the zero setting keeps unknown identifiers out of the cache entirely, so the factory-backed
        // path (which always persists the factory's result) must not be used at all
        _options.UnknownIdentifierCacheExpiration = TimeSpan.Zero;
        _store.FindByIdentifierAsync("ghost", AbortToken).Returns((TenantInfo?)null);

        // when
        var outcome = await _sut.ResolveAsync("ghost", AbortToken);

        // then
        outcome.Kind.Should().Be(TenantResolutionKind.Unknown);
        await _identifierCache.Received(1).GetAsync(Arg.Any<string>(), AbortToken);
        await _identifierCache
            .DidNotReceive()
            .GetOrAddAsync(
                Arg.Any<string>(),
                _AnyIdentifierFactory(),
                _ExpectedEntryOptions,
                Arg.Any<CancellationToken>()
            );
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
    public async Task should_still_cache_a_found_tenant_when_negative_caching_is_disabled()
    {
        // given — disabling negative caching must not stop positive entries from being cached
        _options.UnknownIdentifierCacheExpiration = TimeSpan.Zero;
        var tenant = new TenantInfo("ten_1", "acme", "Acme", isEnabled: true);
        _store.FindByIdentifierAsync("acme", AbortToken).Returns(tenant);

        // when
        var outcome = await _sut.ResolveAsync("acme", AbortToken);

        // then
        outcome.Tenant.Should().BeSameAs(tenant);
        await _identifierCache
            .Received(1)
            .UpsertAsync(
                TenantIdentifierCacheItem.CalculateCacheKey("acme"),
                Arg.Is<TenantIdentifierCacheItem>(item => item.TenantId == "ten_1"),
                _options.CacheExpiration,
                AbortToken
            );
    }

    [Fact]
    public async Task should_hit_the_store_again_after_the_negative_entry_expires()
    {
        // given — the cold arrangement runs the factory on every call, simulating the negative entry
        // having expired between them
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
