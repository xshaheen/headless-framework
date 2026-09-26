// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests.DataPlacement;

public sealed class CachingTenantDataPlacementResolverTests : TestBase
{
    private const string _TenantId = "tenant-a";
    private static readonly string _CacheKey = TenantDataPlacementCacheItem.CalculateCacheKey(_TenantId);

    private readonly ITenantDataPlacementResolver _inner = Substitute.For<ITenantDataPlacementResolver>();
    private readonly IInMemoryCache _cache = Substitute.For<IInMemoryCache>();
    private readonly TenantDataPlacementOptions _options = new() { CacheExpiration = TimeSpan.FromMinutes(3) };
    private readonly CachingTenantDataPlacementResolver<ITenantDataPlacementResolver> _sut;

    public CachingTenantDataPlacementResolverTests()
    {
        _sut = new CachingTenantDataPlacementResolver<ITenantDataPlacementResolver>(
            _inner,
            _cache,
            Options.Create(_options),
            NullLogger<CachingTenantDataPlacementResolver<ITenantDataPlacementResolver>>.Instance
        );

        _cache
            .GetAsync<TenantDataPlacementCacheItem>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CacheValue<TenantDataPlacementCacheItem>.NoValue);
        _cache
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<TenantDataPlacementCacheItem?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
    }

    [Fact]
    public async Task should_resolve_through_inner_and_cache_the_placement_on_a_miss()
    {
        // given
        var placement = new TenantDataPlacement("tenant_a", connectionString: null);
        _inner.ResolveAsync(_TenantId, Arg.Any<CancellationToken>()).Returns(placement);

        // when
        var result = await _sut.ResolveAsync(_TenantId, AbortToken);

        // then
        result.Should().BeSameAs(placement);
        await _cache
            .Received(1)
            .UpsertAsync(
                _CacheKey,
                Arg.Is<TenantDataPlacementCacheItem?>(item => item!.Placement == placement),
                _options.CacheExpiration,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_serve_a_cache_hit_without_calling_inner()
    {
        // given
        var placement = new TenantDataPlacement("tenant_a", connectionString: null);
        _cache
            .GetAsync<TenantDataPlacementCacheItem>(_CacheKey, Arg.Any<CancellationToken>())
            .Returns(new CacheValue<TenantDataPlacementCacheItem>(new(placement), hasValue: true));

        // when
        var result = await _sut.ResolveAsync(_TenantId, AbortToken);

        // then
        result.Should().BeSameAs(placement);
        await _inner.DidNotReceiveWithAnyArgs().ResolveAsync(default!, AbortToken);
    }

    [Fact]
    public async Task should_not_cache_a_missing_placement()
    {
        // given
        _inner.ResolveAsync(_TenantId, Arg.Any<CancellationToken>()).Returns((TenantDataPlacement?)null);

        // when
        var first = await _sut.ResolveAsync(_TenantId, AbortToken);
        var second = await _sut.ResolveAsync(_TenantId, AbortToken);

        // then
        first.Should().BeNull();
        second.Should().BeNull();
        await _inner.Received(2).ResolveAsync(_TenantId, Arg.Any<CancellationToken>());
        await _cache
            .DidNotReceiveWithAnyArgs()
            .UpsertAsync<TenantDataPlacementCacheItem>(default!, default, default, AbortToken);
    }

    [Fact]
    public async Task should_degrade_a_cache_read_fault_to_a_miss()
    {
        // given
        var placement = new TenantDataPlacement(schema: null, "Host=db;Database=tenant_a");
        _cache
            .GetAsync<TenantDataPlacementCacheItem>(_CacheKey, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache down"));
        _inner.ResolveAsync(_TenantId, Arg.Any<CancellationToken>()).Returns(placement);

        // when
        var result = await _sut.ResolveAsync(_TenantId, AbortToken);

        // then
        result.Should().BeSameAs(placement);
    }

    [Fact]
    public async Task should_swallow_a_cache_write_fault()
    {
        // given
        var placement = new TenantDataPlacement("tenant_a", connectionString: null);
        _inner.ResolveAsync(_TenantId, Arg.Any<CancellationToken>()).Returns(placement);
        _cache
            .UpsertAsync(
                Arg.Any<string>(),
                Arg.Any<TenantDataPlacementCacheItem?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("cache down"));

        // when
        var result = await _sut.ResolveAsync(_TenantId, AbortToken);

        // then
        result.Should().BeSameAs(placement);
    }

    [Fact]
    public async Task should_propagate_a_resolver_fault_unwrapped()
    {
        // given
        var fault = new TimeoutException("store down");
        _inner.ResolveAsync(_TenantId, Arg.Any<CancellationToken>()).ThrowsAsync(fault);

        // when
        var act = () => _sut.ResolveAsync(_TenantId, AbortToken);

        // then
        (await act.Should().ThrowAsync<TimeoutException>())
            .Which.Should()
            .BeSameAs(fault);
    }

    [Fact]
    public async Task should_propagate_cancellation_from_the_cache()
    {
        // given
        _cache
            .GetAsync<TenantDataPlacementCacheItem>(_CacheKey, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // when
        var act = () => _sut.ResolveAsync(_TenantId, AbortToken);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _inner.DidNotReceiveWithAnyArgs().ResolveAsync(default!, AbortToken);
    }

    [Fact]
    public async Task should_propagate_cancellation_from_the_resolver()
    {
        // given
        _inner.ResolveAsync(_TenantId, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // when
        var act = () => _sut.ResolveAsync(_TenantId, AbortToken);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
