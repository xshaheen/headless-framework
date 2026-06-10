// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

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
    public async Task simple_overload_should_throw_when_default_entry_options_is_null()
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
    public async Task context_overload_should_throw_when_default_entry_options_is_null()
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
    public async Task simple_overload_should_flow_default_entry_options_when_present()
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
    public async Task context_overload_should_flow_default_entry_options_when_present()
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
}
