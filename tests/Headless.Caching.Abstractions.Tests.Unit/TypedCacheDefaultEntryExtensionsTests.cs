// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Tests for option-less <c>GetOrAddAsync</c> extension overloads on <see cref="ICache{T}"/>
/// driven by <see cref="ICache{T}.DefaultEntryOptions"/>, and for <c>DefaultEntryOptions</c>
/// delegation in <see cref="Cache{T}"/> and <see cref="ScopedCache{T}"/>.
/// </summary>
public sealed class TypedCacheDefaultEntryExtensionsTests : TestBase
{
    private static readonly Func<CancellationToken, ValueTask<string?>> _SimpleFactory = _ =>
        ValueTask.FromResult<string?>("value");

    private static readonly Func<
        CacheFactoryContext<string>,
        CancellationToken,
        ValueTask<CacheFactoryResult<string>>
    > _ContextFactory = (context, _) => ValueTask.FromResult(context.Modified("value"));

    // ── ICache<T>.DefaultEntryOptions delegation ─────────────────────────

    [Fact]
    public void should_delegate_default_entry_options_to_inner_cache_when_cache_t()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };
        var inner = Substitute.For<ICache>();
        inner.DefaultEntryOptions.Returns(options);

        ICache<string> typed = new Cache<string>(inner);

        // when / then — value equality is sufficient; the property must pass through
        typed.DefaultEntryOptions.Should().BeEquivalentTo(options);
    }

    [Fact]
    public void should_expose_null_default_entry_options_when_cache_t_inner_has_none()
    {
        // given
        var inner = Substitute.For<ICache>();
        inner.DefaultEntryOptions.Returns((CacheEntryOptions?)null);

        ICache<string> typed = new Cache<string>(inner);

        // when / then
        typed.DefaultEntryOptions.Should().BeNull();
    }

    [Fact]
    public void should_delegate_default_entry_options_to_inner_cache_when_scoped_cache()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(3) };
        var inner = Substitute.For<ICache>();
        inner.DefaultEntryOptions.Returns(options);

        ICache<string> sut = new ScopedCache<string>(inner, () => "scope");

        // when / then — value equality is sufficient; the property must pass through
        sut.DefaultEntryOptions.Should().BeEquivalentTo(options);
    }

    [Fact]
    public void should_expose_null_default_entry_options_when_scoped_cache_inner_has_none()
    {
        // given
        var inner = Substitute.For<ICache>();
        inner.DefaultEntryOptions.Returns((CacheEntryOptions?)null);

        ICache<string> sut = new ScopedCache<string>(inner, () => "scope");

        // when / then
        sut.DefaultEntryOptions.Should().BeNull();
    }

    // ── Extension: simple factory overload ───────────────────────────────

    [Fact]
    public async Task should_throw_when_typed_simple_overload_default_entry_options_is_null()
    {
        // given
        var cache = Substitute.For<ICache<string>>();
        cache.DefaultEntryOptions.Returns((CacheEntryOptions?)null);

        // when
        var act = async () => await cache.GetOrAddAsync("key", _SimpleFactory, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DefaultEntryOptions*");
    }

    [Fact]
    public async Task should_throw_when_typed_context_overload_default_entry_options_is_null()
    {
        // given
        var cache = Substitute.For<ICache<string>>();
        cache.DefaultEntryOptions.Returns((CacheEntryOptions?)null);

        // when
        var act = async () => await cache.GetOrAddAsync("key", _ContextFactory, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DefaultEntryOptions*");
    }

    [Fact]
    public async Task should_flow_default_entry_options_when_typed_simple_overload_present()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), IsFailSafeEnabled = true };
        var cache = Substitute.For<ICache<string>>();
        cache.DefaultEntryOptions.Returns(options);

        // when
        await cache.GetOrAddAsync("key", _SimpleFactory, AbortToken);

        // then
        await cache.Received(1).GetOrAddAsync("key", _SimpleFactory, options, AbortToken);
    }

    [Fact]
    public async Task should_flow_default_entry_options_when_typed_context_overload_present()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10) };
        var cache = Substitute.For<ICache<string>>();
        cache.DefaultEntryOptions.Returns(options);

        // when
        await cache.GetOrAddAsync("key", _ContextFactory, AbortToken);

        // then
        await cache.Received(1).GetOrAddAsync("key", _ContextFactory, options, AbortToken);
    }

    [Fact]
    public async Task should_apply_default_duration_to_real_cache_entries_when_typed_option_less_overload()
    {
        // given — a real typed cache whose instance default is a 5-minute duration
        var timeProvider = new FakeTimeProvider();
#pragma warning disable CA2000 // underlying cache is owned by Cache<T> for this test
        var inner = new InMemoryCache(
            timeProvider,
            new InMemoryCacheOptions
            {
                DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            }
        );
#pragma warning restore CA2000
        ICache<string> typed = new Cache<string>(inner);
        var key = Faker.Random.AlphaNumeric(10);
        var factoryCalls = 0;

        ValueTask<string?> factory(CancellationToken ct)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("value");
        }

        // when / then — within the default duration the entry stays fresh …
        await typed.GetOrAddAsync(key, factory, AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(4));
        var within = await typed.GetOrAddAsync(key, factory, AbortToken);
        within.Value.Should().Be("value");
        factoryCalls.Should().Be(1, "the entry must still be fresh within the default 5-minute duration");

        // … and right after it the entry expires
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await typed.GetOrAddAsync(key, factory, AbortToken);
        factoryCalls.Should().Be(2, "the default duration must bound the entry's lifetime");
    }
}
