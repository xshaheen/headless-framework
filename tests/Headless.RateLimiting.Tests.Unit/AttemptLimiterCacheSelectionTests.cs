// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class AttemptLimiterCacheSelectionTests : TestBase
{
    private const string _CacheName = "limits";

    private static readonly string _SubjectKey = Convert.ToBase64String(new byte[32]);
    private static readonly AttemptQuota _Quota = new(5, TimeSpan.FromMinutes(1));

    [Fact]
    public async Task should_count_in_the_named_cache_when_cache_name_is_set()
    {
        // given
        await using var provider = _Build(cacheName: _CacheName, addNamedCache: true);
        await _StartAsync(provider);
        var limiter = provider.GetRequiredService<IAttemptLimiter>();

        // when
        await limiter.AcquireAsync("otp-delivery", "+201000000000", _Quota, AbortToken);
        var result = await limiter.AcquireAsync("otp-delivery", "+201000000000", _Quota, AbortToken);

        // then
        result.Count.Should().Be(2);
        var named = provider.GetRequiredKeyedService<ICache>(_CacheName);
        var fallback = provider.GetRequiredService<ICache>();
        (await named.ExistsAsync(result.ResetToken!, AbortToken)).Should().BeTrue();
        (await fallback.ExistsAsync(result.ResetToken!, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_count_in_the_default_cache_when_cache_name_is_null()
    {
        // given
        await using var provider = _Build(cacheName: null, addNamedCache: true);
        await _StartAsync(provider);
        var limiter = provider.GetRequiredService<IAttemptLimiter>();

        // when
        var result = await limiter.AcquireAsync("otp-delivery", "+201000000000", _Quota, AbortToken);

        // then
        var named = provider.GetRequiredKeyedService<ICache>(_CacheName);
        var fallback = provider.GetRequiredService<ICache>();
        (await fallback.ExistsAsync(result.ResetToken!, AbortToken)).Should().BeTrue();
        (await named.ExistsAsync(result.ResetToken!, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_startup_when_the_named_cache_is_not_registered()
    {
        // given
        await using var provider = _Build(cacheName: "missing", addNamedCache: true);

        // when
        var act = () => _StartAsync(provider);

        // then
        (await act.Should().ThrowAsync<MissingRequiredServiceException>())
            .WithMessage("*attempt limiter cache 'missing' is not registered*setup.AddNamed(\"missing\", ...)*")
            .Which.MissingServices.Should()
            .ContainSingle(missing => missing.ServiceType == typeof(ICache));
    }

    [Fact]
    public async Task should_fail_startup_when_no_default_cache_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAttemptLimiter(options => options.SubjectKey = _SubjectKey);
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => _StartAsync(provider);

        // then
        (await act.Should().ThrowAsync<MissingRequiredServiceException>()).WithMessage(
            "*ICache is not registered*AddHeadlessCaching*"
        );
    }

    [Fact]
    public async Task should_count_without_a_default_cache_when_the_named_cache_is_registered()
    {
        // given - only a keyed cache: AddHeadlessCaching always registers a default, so register it directly
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ICache>(
            _CacheName,
            static (_, _) => new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions())
        );
        services.AddAttemptLimiter(options =>
        {
            options.SubjectKey = _SubjectKey;
            options.CacheName = _CacheName;
        });
        await using var provider = services.BuildServiceProvider();

        // when
        await _StartAsync(provider);
        var result = await provider
            .GetRequiredService<IAttemptLimiter>()
            .AcquireAsync("otp-delivery", "+201000000000", _Quota, AbortToken);

        // then
        result.Count.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task should_refuse_a_blank_cache_name(string cacheName)
    {
        // given
        await using var provider = _Build(cacheName, addNamedCache: false);

        // when
        var act = () => _StartAsync(provider);

        // then
        await act.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>();
    }

    private static ServiceProvider _Build(string? cacheName, bool addNamedCache)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCaching(setup =>
        {
            setup.UseInMemory();

            if (addNamedCache)
            {
                setup.AddNamed(_CacheName, i => i.UseInMemory(_ => { }));
            }
        });
        services.AddAttemptLimiter(options =>
        {
            options.SubjectKey = _SubjectKey;
            options.CacheName = cacheName;
        });

        return services.BuildServiceProvider();
    }

    private static async Task _StartAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(AbortToken);
        }
    }
}
