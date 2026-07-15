// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Caching;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>Tests for named Redis cache instances coexisting with the default registration.</summary>
[Collection(nameof(RedisCacheFixture))]
public sealed class NamedRedisCacheTests(RedisCacheFixture fixture) : TestBase
{
    [Fact]
    public async Task should_be_isolated_by_prefix_and_honor_default_entry_options_when_named_redis_cache()
    {
        // given - a default Redis cache plus a named instance with its own prefix and entry defaults
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);

            setup.AddNamed(
                "tenant",
                instance =>
                    instance.UseRedis(options =>
                    {
                        options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                        options.KeyPrefix = "named-tenant:";
                        options.DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(7) };
                    })
            );
        });

        using var host = builder.Build();
        await host.StartAsync(AbortToken);

        var cacheProvider = host.Services.GetRequiredService<ICacheProvider>();
        var named = cacheProvider.GetCache("tenant");
        var defaultCache = host.Services.GetRequiredService<ICache>();
        named.Should().NotBeSameAs(defaultCache);

        // when - write through the named instance
        var key = Faker.Random.AlphaNumeric(12);
        await named.UpsertAsync(key, "named-value", TimeSpan.FromMinutes(5), AbortToken);

        // then - the default (unprefixed) cache does not see it, and the prefixed key exists in Redis
        (await defaultCache.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await named.GetAsync<string>(key, AbortToken)).Value.Should().Be("named-value");
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        (await db.KeyExistsAsync("named-tenant:" + key)).Should().BeTrue();

        // then - the no-options GetOrAddAsync extension applies the named instance's defaults
        var factoryKey = Faker.Random.AlphaNumeric(12);
        var result = await named.GetOrAddAsync<string>(
            factoryKey,
            _ => ValueTask.FromResult<string?>("factory-value"),
            AbortToken
        );
        result.Value.Should().Be("factory-value");

        var expiration = await named.GetExpirationAsync(factoryKey, AbortToken);
        expiration.Should().NotBeNull();
        expiration!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(7), TimeSpan.FromSeconds(30));

        // then - the default cache has no DefaultEntryOptions, so the option-less overload throws
        var act = async () =>
            await defaultCache.GetOrAddAsync<string>(key, _ => ValueTask.FromResult<string?>("v"), AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DefaultEntryOptions*");

        await host.StopAsync(AbortToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_use_configured_serializer_regardless_of_call_order_when_named_redis_cache(
        bool configureSerializerBeforeProvider
    )
    {
        // given
        var instanceName = $"serializer-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{instanceName}:";
        var serializer = new PrefixIntSerializer("custom:");
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);
            setup.AddNamed(
                instanceName,
                instance =>
                {
                    if (configureSerializerBeforeProvider)
                    {
                        instance.WithSerializer(serializer);
                    }

                    instance.UseRedis(options =>
                    {
                        options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                        options.KeyPrefix = keyPrefix;
                    });

                    if (!configureSerializerBeforeProvider)
                    {
                        instance.WithSerializer(serializer);
                    }
                }
            );
        });

        using var host = builder.Build();
        await host.StartAsync(AbortToken);

        var cacheProvider = host.Services.GetRequiredService<ICacheProvider>();
        var named = cacheProvider.GetCache(instanceName);
        var defaultCache = host.Services.GetRequiredService<ICache>();
        var key = Faker.Random.AlphaNumeric(12);
        var defaultKey = $"default-{Faker.Random.AlphaNumeric(12)}";

        // when
        await named.UpsertAsync(key, 123, TimeSpan.FromMinutes(5), AbortToken);
        await defaultCache.UpsertAsync(defaultKey, 456, TimeSpan.FromMinutes(5), AbortToken);

        // then
        (await named.GetAsync<int>(key, AbortToken))
            .Value.Should()
            .Be(123);
        (await defaultCache.GetAsync<int>(defaultKey, AbortToken)).Value.Should().Be(456);

        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var namedStored = await db.StringGetAsync(keyPrefix + key);
        var defaultStored = await db.StringGetAsync(defaultKey);

        RedisCacheEntryFrame
            .Decode(namedStored)
            .ValueSegment.ToArray()
            .Should()
            .Equal(Encoding.UTF8.GetBytes("custom:123"));
        RedisCacheEntryFrame
            .Decode(defaultStored)
            .ValueSegment.ToArray()
            .Should()
            .NotEqual(Encoding.UTF8.GetBytes("custom:456"));

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_resolve_serializer_from_generic_overload_when_named_redis_cache()
    {
        // given - the generic WithSerializer<TSerializer>() overload (ActivatorUtilities-resolved)
        var instanceName = $"gen-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{instanceName}:";
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);
            setup.AddNamed(
                instanceName,
                instance =>
                {
                    instance.UseRedis(options =>
                    {
                        options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                        options.KeyPrefix = keyPrefix;
                    });
                    instance.WithSerializer<FixedPrefixIntSerializer>();
                }
            );
        });

        using var host = builder.Build();
        await host.StartAsync(AbortToken);

        var named = host.Services.GetRequiredService<ICacheProvider>().GetCache(instanceName);
        var key = Faker.Random.AlphaNumeric(12);

        // when
        await named.UpsertAsync(key, 77, TimeSpan.FromMinutes(5), AbortToken);

        // then - the value segment was encoded by the generic-resolved serializer
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var stored = await db.StringGetAsync(keyPrefix + key);
        RedisCacheEntryFrame.Decode(stored).ValueSegment.ToArray().Should().Equal(Encoding.UTF8.GetBytes("gen:77"));
        (await named.GetAsync<int>(key, AbortToken)).Value.Should().Be(77);

        await host.StopAsync(AbortToken);
    }

    private sealed class FixedPrefixIntSerializer() : PrefixIntSerializer("gen:");

    private class PrefixIntSerializer(string prefix) : ISerializer
    {
        public T? Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            if (typeof(T) != typeof(int))
            {
                throw new NotSupportedException();
            }

            var stored = Encoding.UTF8.GetString(data.Span);
            return (T?)(object?)int.Parse(stored.AsSpan(prefix.Length), CultureInfo.InvariantCulture);
        }

        public T? Deserialize<T>(in ReadOnlySequence<byte> data) => Deserialize<T>(data.ToArray());

        public void Serialize<T>(T value, IBufferWriter<byte> output)
        {
            if (value is not int number)
            {
                throw new NotSupportedException();
            }

            output.Write(Encoding.UTF8.GetBytes(prefix + number.ToString(CultureInfo.InvariantCulture)));
        }

        public void Serialize(object? value, IBufferWriter<byte> output)
        {
            if (value is not int number)
            {
                throw new NotSupportedException();
            }

            output.Write(Encoding.UTF8.GetBytes(prefix + number.ToString(CultureInfo.InvariantCulture)));
        }

        public object Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            if (type != typeof(int))
            {
                throw new NotSupportedException();
            }

            var stored = Encoding.UTF8.GetString(data.Span);
            return int.Parse(stored.AsSpan(prefix.Length), CultureInfo.InvariantCulture);
        }

        public object Deserialize(in ReadOnlySequence<byte> data, Type type) => Deserialize(data.ToArray(), type);
    }
}
