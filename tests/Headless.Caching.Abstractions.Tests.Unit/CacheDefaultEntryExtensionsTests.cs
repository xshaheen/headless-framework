// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>Tests for the option-less <c>GetOrAddAsync</c> extension overloads driven by <see cref="ICache.DefaultEntryOptions"/>.</summary>
public sealed class CacheDefaultEntryExtensionsTests : TestBase
{
    private static readonly Func<CancellationToken, ValueTask<string?>> _SimpleFactory = _ =>
        ValueTask.FromResult<string?>("value");

    private static readonly Func<
        CacheFactoryContext<string>,
        CancellationToken,
        ValueTask<CacheFactoryResult<string>>
    > _ContextFactory = (context, _) => ValueTask.FromResult(context.Modified("value"));

    [Fact]
    public async Task should_throw_when_simple_overload_default_entry_options_is_null()
    {
        // given
        var cache = Substitute.For<ICache>();
        cache.DefaultEntryOptions.Returns((CacheEntryOptions?)null);

        // when
        var act = async () => await cache.GetOrAddAsync("key", _SimpleFactory, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DefaultEntryOptions*");
    }

    [Fact]
    public async Task should_throw_when_context_overload_default_entry_options_is_null()
    {
        // given
        var cache = Substitute.For<ICache>();
        cache.DefaultEntryOptions.Returns((CacheEntryOptions?)null);

        // when
        var act = async () => await cache.GetOrAddAsync("key", _ContextFactory, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DefaultEntryOptions*");
    }

    [Fact]
    public async Task should_flow_default_entry_options_when_simple_overload_present()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), IsFailSafeEnabled = true };
        var cache = Substitute.For<ICache>();
        cache.DefaultEntryOptions.Returns(options);

        // when
        await cache.GetOrAddAsync("key", _SimpleFactory, AbortToken);

        // then
        await cache.Received(1).GetOrAddAsync("key", _SimpleFactory, options, AbortToken);
    }

    [Fact]
    public async Task should_flow_default_entry_options_when_context_overload_present()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10) };
        var cache = Substitute.For<ICache>();
        cache.DefaultEntryOptions.Returns(options);

        // when
        await cache.GetOrAddAsync("key", _ContextFactory, AbortToken);

        // then
        await cache.Received(1).GetOrAddAsync("key", _ContextFactory, options, AbortToken);
    }

    [Fact]
    public async Task should_apply_default_duration_to_real_cache_entries_when_no_options_overload()
    {
        // given — a real cache whose instance default is a 5-minute duration
        var timeProvider = new FakeTimeProvider();
        using var cache = new InMemoryCache(
            timeProvider,
            new InMemoryCacheOptions
            {
                DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            }
        );
        var key = Faker.Random.AlphaNumeric(10);
        var factoryCalls = 0;

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("value");
        }

        // when / then — within the default duration the entry stays fresh (factory not re-invoked)…
        await cache.GetOrAddAsync(key, factory, AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(4));
        var withinDefault = await cache.GetOrAddAsync(key, factory, AbortToken);
        withinDefault.Value.Should().Be("value");
        factoryCalls.Should().Be(1, "the entry must still be fresh within the default 5-minute duration");

        // …and right after it the entry expires, proving the DEFAULT duration actually governed the write
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await cache.GetOrAddAsync(key, factory, AbortToken);
        factoryCalls.Should().Be(2, "the default duration must bound the entry's lifetime");
    }

    [Fact]
    public async Task should_beat_default_entry_options_when_per_call_options()
    {
        // given — a real cache with a LONG instance default and a SHORT per-call duration
        var timeProvider = new FakeTimeProvider();
        using var cache = new InMemoryCache(
            timeProvider,
            new InMemoryCacheOptions
            {
                DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
            }
        );
        var key = Faker.Random.AlphaNumeric(10);
        var perCallOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1) };
        var factoryCalls = 0;

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("value");
        }

        // when — the write goes through the options overload, then time passes beyond the per-call duration
        // but well within the instance default
        await cache.GetOrAddAsync(key, factory, perCallOptions, AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await cache.GetOrAddAsync(key, factory, perCallOptions, AbortToken);

        // then — the factory re-ran: the per-call 1-minute duration governed, not the 10-minute default
        factoryCalls.Should().Be(2);
    }
}
