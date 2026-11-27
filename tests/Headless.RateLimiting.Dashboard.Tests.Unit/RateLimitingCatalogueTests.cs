// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Caching;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests;

public sealed class RateLimitingCatalogueTests : TestBase
{
    private const string _Catalogue = RateLimitingHost.Api + "/catalogue";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 26, 14, 20, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_list_the_declared_purposes_by_name_with_the_shortest_window()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            limiter: static options =>
            {
                options.Purposes["password-reset.request"] = new AttemptQuota(3, TimeSpan.FromHours(1));
                options.Purposes["otp.verify"] = new AttemptQuota(3, TimeSpan.FromMinutes(10));
            }
        );

        // when
        var body = await _ReadAsync(app);

        // then
        body.GetProperty("purposes")
            .EnumerateArray()
            .Select(static purpose =>
                (
                    purpose.GetProperty("name").GetString(),
                    purpose.GetProperty("limit").GetInt32(),
                    purpose.GetProperty("windowSeconds").GetInt64()
                )
            )
            .Should()
            .Equal(("otp.verify", 3, 600L), ("password-reset.request", 3, 3600L), ("sign-in.password", 5, 900L));
        body.GetProperty("shortest").GetProperty("name").GetString().Should().Be("otp.verify");
        body.GetProperty("counterStore").GetString().Should().Be("default");
    }

    [Fact]
    public async Task should_name_the_named_cache_when_the_limiter_counts_in_one()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            limiter: static options => options.CacheName = "attempts",
            caching: static setup => setup.AddNamed("attempts", i => i.UseInMemory(_ => { }))
        );

        // when
        var body = await _ReadAsync(app);

        // then
        body.GetProperty("counterStore").GetString().Should().Be("named");
        body.GetProperty("cacheName").GetString().Should().Be("attempts");
    }

    [Fact]
    public async Task should_name_the_remote_cache_when_one_is_registered()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: static services => services.AddSingleton(Substitute.For<IRemoteCache>())
        );

        // when
        var body = await _ReadAsync(app);

        // then
        body.GetProperty("counterStore").GetString().Should().Be("remote");
    }

    [Fact]
    public async Task should_omit_the_counter_store_under_a_custom_limiter()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: static services => services.AddSingleton(Substitute.For<IAttemptLimiter>())
        );

        // when
        var body = await _ReadAsync(app);

        // then
        body.TryGetProperty("counterStore", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_count_the_declared_purposes_in_the_platform_sidebar()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            limiter: static options => options.Purposes["otp.verify"] = new AttemptQuota(3, TimeSpan.FromMinutes(10))
        );
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await RateLimitingHost.GetAsync(
            client,
            "/headless/api/counts?product=platform",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var count = (await RateLimitingHost.ReadAsync(response, AbortToken))
            .GetProperty("counts")
            .GetProperty("purposes");
        count.GetProperty("count").GetInt64().Should().Be(2);
        count.GetProperty("capped").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_list_no_purposes_and_count_zero_when_none_is_declared()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            limiter: static options => options.Purposes.Clear()
        );
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "viewer", AbortToken);

        // when
        var body = await _ReadAsync(app);
        using var counts = await RateLimitingHost.GetAsync(
            client,
            "/headless/api/counts?product=platform",
            cookie,
            AbortToken
        );

        // then
        body.GetProperty("purposes").GetArrayLength().Should().Be(0);
        body.TryGetProperty("shortest", out _).Should().BeFalse();
        (await RateLimitingHost.ReadAsync(counts, AbortToken))
            .GetProperty("counts")
            .GetProperty("purposes")
            .GetProperty("count")
            .GetInt64()
            .Should()
            .Be(0);
    }

    private static async Task<JsonElement> _ReadAsync(Microsoft.AspNetCore.Builder.WebApplication app)
    {
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "viewer", AbortToken);
        using var response = await RateLimitingHost.GetAsync(client, _Catalogue, cookie, AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await RateLimitingHost.ReadAsync(response, AbortToken);
    }
}
