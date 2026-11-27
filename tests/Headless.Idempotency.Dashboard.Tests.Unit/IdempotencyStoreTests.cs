// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class IdempotencyStoreTests : TestBase
{
    private const string _Store = IdempotencyHost.Api + "/store";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 14, 20, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_describe_the_in_memory_store_with_its_configured_defaults_and_no_location()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(
            _time,
            AbortToken,
            options: static options =>
            {
                options.DefaultRetention = TimeSpan.FromHours(12);
                options.DefaultLeaseDuration = TimeSpan.FromMinutes(3);
                options.MinimumLeaseDuration = TimeSpan.FromSeconds(2);
                options.MaximumLeaseDuration = TimeSpan.FromHours(2);
                options.PurgeInterval = TimeSpan.FromMinutes(30);
                options.PurgeBatchSize = 250;
            }
        );
        using var client = IdempotencyHost.Client(app);
        var cookie = await IdempotencyHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await IdempotencyHost.GetAsync(client, _Store, cookie, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await IdempotencyHost.ReadAsync(response, AbortToken);
        body.GetProperty("provider").GetString().Should().Be("Headless.Idempotency.InMemory");
        body.TryGetProperty("location", out _).Should().BeFalse();
        body.GetProperty("defaultRetentionSeconds").GetInt64().Should().Be(12 * 3600);
        body.GetProperty("defaultLeaseSeconds").GetInt64().Should().Be(180);
        body.GetProperty("minimumLeaseSeconds").GetInt64().Should().Be(2);
        body.GetProperty("maximumLeaseSeconds").GetInt64().Should().Be(7200);
        body.GetProperty("purgeIntervalSeconds").GetInt64().Should().Be(1800);
        body.GetProperty("purgeBatchSize").GetInt32().Should().Be(250);
    }

    [Fact]
    public async Task should_describe_the_cache_store_with_its_key_prefix_and_no_purge_interval()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken, store: IdempotencyTestStore.Cache);
        using var client = IdempotencyHost.Client(app);
        var cookie = await IdempotencyHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await IdempotencyHost.GetAsync(client, _Store, cookie, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await IdempotencyHost.ReadAsync(response, AbortToken);
        body.GetProperty("provider").GetString().Should().Be("Headless.Idempotency.Caching");
        body.GetProperty("location").GetString().Should().NotBeNullOrWhiteSpace();
        body.TryGetProperty("purgeIntervalSeconds", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_refuse_the_store_without_a_session()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);
        using var client = IdempotencyHost.Client(app);

        // when
        using var response = await IdempotencyHost.GetAsync(client, _Store, cookie: null, AbortToken);

        // then
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
