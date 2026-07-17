// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>Tests for named hybrid cache registration and named tier binding.</summary>
public sealed class NamedHybridCacheTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private ServiceCollection _CreateBaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddSingleton(Substitute.For<IBus>());

        return services;
    }

    [Fact]
    public async Task should_bind_named_tiers_when_named_hybrid()
    {
        // given
        var services = _CreateBaseServices();
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseHybrid(options =>
                    {
                        options.LocalCacheName = "tenant-l1";
                        options.RemoteCacheName = "tenant-l2";
                    })
            );
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var cache = provider.GetRequiredKeyedService<ICache>("tenant");

        // then
        var hybrid = cache.Should().BeOfType<HybridCache>().Subject;
        hybrid.LocalCache.Should().BeSameAs(l1);

        await hybrid.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        (await l1.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
        (await l2Inner.GetAsync<string>("key", AbortToken)).Value.Should().Be("value");
    }

    [Fact]
    public async Task should_publish_invalidation_with_cache_name_when_named_hybrid()
    {
        // given
        var services = _CreateBaseServices();
        var bus = services
            .Single(x => x.ServiceType == typeof(IBus))
            .ImplementationInstance.Should()
            .BeAssignableTo<IBus>()
            .Subject;
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseHybrid(options =>
                    {
                        options.LocalCacheName = "tenant-l1";
                        options.RemoteCacheName = "tenant-l2";
                    })
            );
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredKeyedService<ICache>("tenant");

        // when
        await cache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // then
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(message => message.CacheName == "tenant" && message.Key == "key"),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    // Issue #693: a setupAction that tries to override the public CacheName must not change invalidation
    // routing. Setup.cs applies InvalidationRoutingName = name AFTER setupAction runs, so the registration name
    // stays authoritative for routing even though CacheName itself does get overwritten too (both are stamped
    // post-configure).
    [Fact]
    public async Task should_route_by_registration_name_when_named_hybrid_setup_action_overrides_cache_name()
    {
        // given — the setupAction attempts to override the public CacheName
        var services = _CreateBaseServices();
        var bus = services
            .Single(x => x.ServiceType == typeof(IBus))
            .ImplementationInstance.Should()
            .BeAssignableTo<IBus>()
            .Subject;
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseHybrid(options =>
                    {
                        options.LocalCacheName = "tenant-l1";
                        options.RemoteCacheName = "tenant-l2";
                        options.CacheName = "attacker-supplied-name";
                    })
            );
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredKeyedService<ICache>("tenant");

        // when
        await cache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // then — the wire message routes by the registration name, ignoring the setupAction's override
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<CacheInvalidationMessage>(message => message.CacheName == "tenant" && message.Key == "key"),
                Arg.Any<PublishOptions?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_route_named_message_to_named_hybrid_when_invalidation_consumer()
    {
        // given
        var services = _CreateBaseServices();
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseHybrid(options =>
                    {
                        options.InstanceId = "node-b";
                        options.LocalCacheName = "tenant-l1";
                        options.RemoteCacheName = "tenant-l2";
                    })
            );
        });

        await using var provider = services.BuildServiceProvider();
        await l1.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        var consumer = new HybridCacheInvalidationConsumer(
            provider.GetRequiredService<ICacheProvider>(),
            NullLogger<HybridCacheInvalidationConsumer>.Instance
        );

        // when
        await consumer.ConsumeAsync(
            new ConsumeContext<CacheInvalidationMessage>
            {
                IntentType = IntentType.Bus,
                Message = new CacheInvalidationMessage
                {
                    InstanceId = "node-a",
                    CacheName = "tenant",
                    Key = "key",
                },
                MessageId = Faker.Random.Guid().ToString(),
                CorrelationId = null,
                Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
                Timestamp = _timeProvider.GetUtcNow(),
                MessageName = nameof(CacheInvalidationMessage),
            },
            AbortToken
        );

        // then
        var value = await l1.GetAsync<string>("key", AbortToken);
        value.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_bind_named_tiers_when_nameless_hybrid_options_set()
    {
        // given - the options drive tier binding on the default (nameless) path too
        var services = _CreateBaseServices();
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("local-tier", l1);
        services.AddKeyedSingleton<ICache>("remote-tier", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHeadlessCaching(setup =>
            setup.UseHybrid(options =>
            {
                options.LocalCacheName = "local-tier";
                options.RemoteCacheName = "remote-tier";
            })
        );

        await using var provider = services.BuildServiceProvider();

        // when
        var hybrid = provider.GetRequiredService<HybridCache>();

        // then
        hybrid.LocalCache.Should().BeSameAs(l1);
    }

    [Fact]
    public async Task should_fail_clearly_when_named_hybrid_named_tier_is_missing()
    {
        // given
        var services = _CreateBaseServices();
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "broken",
                instance => instance.UseHybrid(options => options.LocalCacheName = "missing-tier")
            );
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredKeyedService<ICache>("broken");

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*LocalCacheName*'missing-tier'*no cache is registered*");
    }

    [Fact]
    public async Task should_fail_clearly_when_named_hybrid_named_tier_has_wrong_shape()
    {
        // given - "remote-only" is an IRemoteCache, not an IInMemoryCache
        var services = _CreateBaseServices();
        using var l2Inner = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        services.AddKeyedSingleton<ICache>("remote-only", new InMemoryRemoteCacheAdapter(l2Inner));
        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "bad-shape",
                instance => instance.UseHybrid(options => options.LocalCacheName = "remote-only")
            );
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredKeyedService<ICache>("bad-shape");

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*LocalCacheName*'remote-only'*{nameof(IInMemoryCache)}*");
    }

    [Fact]
    public async Task should_govern_option_less_get_or_add_over_tier_defaults_when_named_hybrid_default_entry_options()
    {
        // given — named tiers that EACH carry their own (short) defaults, composed under a hybrid with a
        // LONGER default of its own
        var services = _CreateBaseServices();

        using var l1 = new InMemoryCache(
            _timeProvider,
            new InMemoryCacheOptions
            {
                DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1) },
            }
        );

        using var l2Inner = new InMemoryCache(
            _timeProvider,
            new InMemoryCacheOptions
            {
                DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(2) },
            }
        );

        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseHybrid(options =>
                    {
                        options.LocalCacheName = "tenant-l1";
                        options.RemoteCacheName = "tenant-l2";
                        options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10) };
                    })
            );
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredKeyedService<ICache>("tenant");
        var key = Faker.Random.AlphaNumeric(10);
        var factoryCalls = 0;

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("value");
        }

        // then — the option-less surface exposes the HYBRID's own default, not either tier's
        cache.DefaultEntryOptions.Should().NotBeNull();
        cache.DefaultEntryOptions.Value.Duration.Should().Be(TimeSpan.FromMinutes(10));

        // when — past BOTH tier defaults (1 and 2 minutes) but within the hybrid's 10-minute default
        await cache.GetOrAddAsync(key, factory, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(4));
        var withinHybridDefault = await cache.GetOrAddAsync(key, factory, AbortToken);

        // then — still fresh: the hybrid's default governed the write, the tier defaults did not
        withinHybridDefault.Value.Should().Be("value");
        factoryCalls.Should().Be(1, "the hybrid's 10-minute default must govern, not the tiers' 1/2-minute defaults");

        // and past the hybrid's default the entry expires, proving its duration actually bounded the entry
        _timeProvider.Advance(TimeSpan.FromMinutes(7));
        await cache.GetOrAddAsync(key, factory, AbortToken);
        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task should_beat_hybrid_and_tier_defaults_when_per_call_options()
    {
        // given — the same layered defaults (tiers: 1/2 minutes, hybrid: 10 minutes) and a 30-second per-call
        var services = _CreateBaseServices();

        using var l1 = new InMemoryCache(
            _timeProvider,
            new InMemoryCacheOptions
            {
                DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1) },
            }
        );

        using var l2Inner = new InMemoryCache(
            _timeProvider,
            new InMemoryCacheOptions
            {
                DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(2) },
            }
        );
        services.AddKeyedSingleton<ICache>("tenant-l1", l1);
        services.AddKeyedSingleton<ICache>("tenant-l2", new InMemoryRemoteCacheAdapter(l2Inner));

        services.AddHeadlessCaching(setup =>
        {
            setup.RegisterDefaultProvider(CacheConstants.MemoryCacheProvider, static _ => { });
            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseHybrid(options =>
                    {
                        options.LocalCacheName = "tenant-l1";
                        options.RemoteCacheName = "tenant-l2";
                        options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(10) };
                    })
            );
        });

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredKeyedService<ICache>("tenant");
        var key = Faker.Random.AlphaNumeric(10);
        var perCallOptions = new CacheEntryOptions { Duration = TimeSpan.FromSeconds(30) };
        var factoryCalls = 0;

        ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return ValueTask.FromResult<string?>("value");
        }

        // when — 40 seconds later: past the per-call 30s, within every default (1m, 2m, and 10m)
        await cache.GetOrAddAsync(key, factory, perCallOptions, AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(40));
        await cache.GetOrAddAsync(key, factory, perCallOptions, AbortToken);

        // then — the factory re-ran: only the per-call duration can explain the expiry
        factoryCalls.Should().Be(2, "per-call options must beat the hybrid's default and both tier defaults");
    }

    // Reserved-name rejection is owned by AddHeadlessCaching's AddNamed gate and is covered by
    // Headless.Caching.Core.Tests.Unit/CachingSetupBuilderTests.
}
