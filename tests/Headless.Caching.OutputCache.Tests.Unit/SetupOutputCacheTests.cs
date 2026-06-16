// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Tests for the <c>UseOutputCache</c> registration guards and the <c>IOutputCacheStore</c> override.</summary>
public sealed class SetupOutputCacheTests
{
    private const string _CacheName = "output-cache";

    [Fact]
    public void should_reject_a_reserved_cache_name()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.UseOutputCache(
                    options => options.CacheName = "Headless.Caching:Memory",
                    instance => instance.UseInMemory(_ => { })
                )
            );

        // then — AddNamed rejects the reserved provider key
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_blank_cache_name()
    {
        // given
        var services = new ServiceCollection();

        // when — a whitespace cache name is caught eagerly by the Argument guard at the UseOutputCache call site,
        // before any named cache or store is registered
        var action = () =>
            services.AddHeadlessCaching(setup =>
                setup.UseOutputCache(options => options.CacheName = "   ", instance => instance.UseInMemory(_ => { }))
            );

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_serializer_configured_on_the_named_instance()
    {
        // given
        var services = new ServiceCollection();

        // when — byte[] is the cache's native wire format, so a serializer on the instance is meaningless; the guard
        // fails fast rather than silently ignoring it. WithSerializer (Redis package) sets the same SerializerFactory
        // this checks; the factory is never invoked, so a null-returning stub is enough to make it non-null.
        var action = () =>
            services.AddHeadlessCaching(setup =>
            {
                setup.UseInMemory();
                setup.UseOutputCache(
                    options => options.CacheName = _CacheName,
                    instance =>
                    {
                        instance.UseInMemory(_ => { });
                        instance.SetSerializerFactory(static _ => null!);
                    }
                );
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*serializer*");
    }

    [Fact]
    public void should_resolve_the_headless_store_when_add_output_cache_is_called_before_use_output_cache()
    {
        // given — AddOutputCache registers MemoryOutputCacheStore via TryAdd first
        using var provider = _BuildProvider(addOutputCacheFirst: true);

        // when
        var store = provider.GetRequiredService<IOutputCacheStore>();

        // then — Replace beats the earlier TryAdd
        store.Should().BeOfType<HeadlessOutputCacheStore>();
    }

    [Fact]
    public void should_resolve_the_headless_store_when_use_output_cache_is_called_before_add_output_cache()
    {
        // given — the Headless store is registered first; AddOutputCache's TryAdd must then no-op
        using var provider = _BuildProvider(addOutputCacheFirst: false);

        // when
        var store = provider.GetRequiredService<IOutputCacheStore>();

        // then
        store.Should().BeOfType<HeadlessOutputCacheStore>();
    }

    [Fact]
    public void should_register_the_named_cache_keyed_by_cache_name()
    {
        // given
        using var provider = _BuildProvider(addOutputCacheFirst: true);

        // when / then — byte[] is the cache's native wire format, so the store wires no serializer
        provider.GetRequiredKeyedService<ICache>(_CacheName).Should().NotBeNull();
    }

    private static ServiceProvider _BuildProvider(bool addOutputCacheFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (addOutputCacheFirst)
        {
            services.AddOutputCache();
        }

        services.AddHeadlessCaching(setup =>
        {
            setup.UseInMemory();
            setup.UseOutputCache(options => options.CacheName = _CacheName, instance => instance.UseInMemory(_ => { }));
        });

        if (!addOutputCacheFirst)
        {
            services.AddOutputCache();
        }

        return services.BuildServiceProvider();
    }
}
