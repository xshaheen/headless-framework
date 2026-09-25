// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisAttemptLimiterFixture))]
public sealed class RedisAttemptLimiterTests(RedisAttemptLimiterFixture fixture) : TestBase
{
    private static readonly string _SubjectKey = Convert.ToBase64String(
        Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()
    );

    private readonly List<IHost> _hosts = [];
    private readonly List<ConnectionMultiplexer> _connections = [];

    [Fact]
    public async Task should_admit_exactly_the_limit_across_two_processes_sharing_redis()
    {
        // given - two hosts with their own connections stand in for two replicas
        await _FlushAsync();
        var first = await _StartLimiterAsync();
        var second = await _StartLimiterAsync();
        var quota = new AttemptQuota(20, TimeSpan.FromHours(1));
        var subject = Faker.Phone.PhoneNumber("+2010########");

        // when - 40 concurrent attempts, split between the replicas
        var attempts = Enumerable
            .Range(0, 40)
            .Select(i => (i % 2 == 0 ? first : second).AcquireAsync("pin-verify", subject, quota, AbortToken).AsTask());
        var results = await Task.WhenAll(attempts);

        // then
        results.Count(r => r.IsAllowed).Should().Be(20);
        results.Select(r => r.Count).Should().BeEquivalentTo(Enumerable.Range(1, 40).Select(i => (long)i));
    }

    [Fact]
    public async Task should_never_write_the_subject_into_a_cache_key()
    {
        // given
        await _FlushAsync();
        var limiter = await _StartLimiterAsync();
        var quota = new AttemptQuota(5, TimeSpan.FromMinutes(1));
        string[] subjects = ["+201012345678", "someone@example.com", "203.0.113.7", "2001:db8::1"];

        // when
        foreach (var subject in subjects)
        {
            await limiter.AcquireAsync("otp-delivery", subject, quota, AbortToken);
        }

        // then
        var keys = await _ScanKeysAsync();
        keys.Should().HaveCount(subjects.Length);
        keys.Should().AllSatisfy(key => key.Should().Contain("attempts:v1:otp-delivery:60:"));

        foreach (var subject in subjects)
        {
            keys.Should().NotContain(key => key.Contains(subject, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task should_expire_the_counter_when_its_window_closes()
    {
        // given
        await _FlushAsync();
        var limiter = await _StartLimiterAsync();

        // when
        var result = await limiter.AcquireAsync(
            "otp-delivery",
            "+201012345678",
            new AttemptQuota(5, TimeSpan.FromHours(1)),
            AbortToken
        );

        // then
        var key = (await _ScanKeysAsync()).Single();
        var ttl = await _Server().Multiplexer.GetDatabase().KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(result.RetryAfter);
    }

    [Fact]
    public async Task should_clear_the_counter_on_reset()
    {
        // given
        await _FlushAsync();
        var limiter = await _StartLimiterAsync();
        var quota = new AttemptQuota(3, TimeSpan.FromMinutes(1));
        var attempt = await limiter.AcquireAsync("otp-delivery", "+201012345678", quota, AbortToken);

        // when
        await limiter.ResetAsync(attempt, AbortToken);

        // then
        (await _ScanKeysAsync())
            .Should()
            .BeEmpty();
    }

    private async Task<IAttemptLimiter> _StartLimiterAsync()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        _connections.Add(connection);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessCaching(setup =>
            setup.UseRedis(options => options.ConnectionMultiplexer = connection)
        );
        builder.Services.AddAttemptLimiter(options => options.SubjectKey = _SubjectKey);

        var host = builder.Build();
        _hosts.Add(host);
        await host.StartAsync(AbortToken);

        return host.Services.GetRequiredService<IAttemptLimiter>();
    }

    private IServer _Server()
    {
        var connection =
            _connections.Count > 0
                ? _connections[0]
                : throw new InvalidOperationException("Start a limiter before reading the keyspace.");

        return connection.GetServer(connection.GetEndPoints()[0]);
    }

    private async Task<List<string>> _ScanKeysAsync()
    {
        var keys = new List<string>();

        await foreach (var key in _Server().KeysAsync(pattern: "*").WithCancellation(AbortToken))
        {
            keys.Add(key.ToString());
        }

        return keys;
    }

    private async Task _FlushAsync()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        await connection.GetServer(connection.GetEndPoints()[0]).FlushAllDatabasesAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
        }

        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }
}
