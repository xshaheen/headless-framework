// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Connection-bound tests against a fake APNs that serves one HTTP/2 stream per connection, the way APNs starts a
/// token-authenticated connection. The bound is <see cref="ApnsOptions.MaxConnections"/>, enforced with a
/// connect-callback permit because the runtime's HTTP/1.1-only <c>MaxConnectionsPerServer</c> cannot bound an
/// HTTP/2 pool.
/// </summary>
/// <remarks>
/// <para>
/// Measured on this double without the bound: a cold multicast at <c>MaxConcurrency</c> 100 opens roughly 10-12
/// connections and most requests exhaust the runtime's three-retry budget for streams refused during the cold
/// start — every fresh connection admits streams before its SETTINGS frame advertises the one-stream limit, so a
/// racing burst is refused and retried onto another racing connection. With the bound set, a request instead waits
/// for a permit, and a pool whose connections are warm (their SETTINGS exchanged) completes every request while
/// never exceeding the bound. These tests pre-warm to the bound so the cold-start race stays out of the assertion.
/// </para>
/// <para>
/// A request refused with <c>REFUSED_STREAM</c> — the same runtime retry path a GOAWAY that did not process the
/// request triggers — is redelivered on another connection and never duplicated, which the redelivery test covers.
/// </para>
/// </remarks>
public sealed class ApnsConnectionBoundTests : TestBase
{
    [Fact]
    public async Task should_open_many_connections_for_a_cold_burst_when_unbounded()
    {
        // given - a one-stream server and no effective bound. A cold burst at a small concurrency (inside the
        // runtime's three-retry budget for cold-start refused streams) opens one connection per concurrent
        // request, because each connection carries one stream at a time.
        await using var server = await FakeApnsServer.StartOneStreamAsync(AbortToken);
        server.ResponseDelay = TimeSpan.FromMilliseconds(20);
        await using var provider = server.CreateProvider(o =>
        {
            o.MaxConnections = 1000;
            o.MaxConcurrency = 2;
        });
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when
        var result = await service.SendMulticastAsync(
            Enumerable.Range(0, 8).Select(i => $"device-{i}").ToArray(),
            PushNotificationRequests.Valid(),
            AbortToken
        );

        // then - everything was delivered, and the unbounded pool opened one connection per concurrent request
        // (a refused-stream retry may dial one more) rather than serializing on one: the bound exists to cap
        // exactly this growth.
        result.SuccessCount.Should().Be(8);
        server.OpenedConnections.Should().BeInRange(2, 3);
        server.MaxConcurrentConnections.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task should_never_exceed_max_connections_and_complete_every_send_when_bounded()
    {
        // given - the bound is 4 and the pool is pre-warmed to it, so the multicast's requests queue on streams
        // the connections advertised instead of racing cold connections' SETTINGS frames.
        await using var server = await FakeApnsServer.StartOneStreamAsync(AbortToken);
        server.ResponseDelay = TimeSpan.FromMilliseconds(5);
        await using var provider = server.CreateProvider(o => o.MaxConnections = 4);
        var service = provider.GetRequiredService<IPushNotificationService>();

        for (var i = 0; i < 4; i++)
        {
            var warm = await service.SendToDeviceAsync($"warm-{i}", PushNotificationRequests.Valid(), AbortToken);
            warm.IsSucceeded().Should().BeTrue();
        }

        var tokens = Enumerable.Range(0, 100).Select(i => $"device-{i:D3}").ToArray();

        // when
        var result = await service.SendMulticastAsync(tokens, PushNotificationRequests.Valid(), AbortToken);

        // then - the bound held, every request landed, and nothing was duplicated.
        var failures = result.Responses.Where(r => r.IsFailed()).Select(r => r.FailureError).ToArray();
        result.SuccessCount.Should().Be(100, $"failures: {string.Join(" | ", failures)}");
        result.Responses.Select(r => r.ClientIdentifier).Should().Equal(tokens);
        server.MaxConcurrentConnections.Should().BeLessThanOrEqualTo(4);
        server.Requests.Should().HaveCount(104);
        server
            .Requests.GroupBy(r => r.DeviceToken)
            .Should()
            .OnlyContain(g => g.Count() == 1, "a queued request must not be sent twice");
    }

    [Fact]
    public async Task should_still_open_connections_up_to_the_bound_after_dials_fail()
    {
        // given - the first 2 dials are refused the way a restarting server refuses them, then the endpoint
        // answers. The bound is 2, so a permit stranded by a faulted dial would cap the pool at 1 or 0 forever.
        await using var server = await FakeApnsServer.StartOneStreamAsync(AbortToken);
        server.ResponseDelay = TimeSpan.FromMilliseconds(5);
        var dials = new DialRefuser(failures: 2);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
            setup.RegisterDefaultProvider(s =>
                SetupApnsPushNotifications.AddApnsCore(
                    s,
                    name: null,
                    (collection, name) =>
                        collection.Configure<ApnsOptions, ApnsOptionsValidator>(
                            options =>
                            {
                                server.ConfigureOptions(options);
                                options.MaxConnections = 2;
                            },
                            name
                        ),
                    client => client.BaseAddress = server.BaseAddress,
                    resilience => resilience.Retry.Delay = TimeSpan.Zero,
                    handler =>
                    {
                        var inner = handler.ConnectCallback;
                        handler.ConnectCallback = async (context, token) =>
                        {
                            await dials.GateAsync();

                            return await inner!(context, token);
                        };
                    }
                )
            )
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        var service = provider.GetRequiredService<IPushNotificationService>();

        // when - the first 2 dials fault inside the callback, after the permit was taken.
        var result = await service.SendMulticastAsync(
            Enumerable.Range(0, 20).Select(i => $"device-{i:D2}").ToArray(),
            PushNotificationRequests.Valid(),
            AbortToken
        );

        // then - the faulted dials released their permits, so the pool still opens up to the bound and every send
        // completes.
        dials.FailedDials.Should().Be(2);
        result.SuccessCount.Should().Be(20);
        server.MaxConcurrentConnections.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_deliver_refused_requests_again_on_another_connection_and_none_twice()
    {
        // given - one stream per connection: a request the server refuses with REFUSED_STREAM (the client raced
        // the connection's SETTINGS) takes the same runtime retry path a GOAWAY that did not process the request
        // triggers, so this test stands in for that GOAWAY contract: redelivery on a new connection, no
        // duplicates.
        await using var server = await FakeApnsServer.StartOneStreamAsync(AbortToken);
        server.ResponseDelay = TimeSpan.FromMilliseconds(20);
        await using var provider = server.CreateProvider(o => o.MaxConnections = 3);
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var tokens = Enumerable.Range(0, 9).Select(i => $"device-{i}").ToArray();

        // when
        var result = await service.SendMulticastAsync(
            tokens,
            new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
            AbortToken
        );

        // then - every token was delivered exactly once: the ones the first connections did not process arrived
        // on another connection, and none is duplicated.
        result.SuccessCount.Should().Be(9);
        var counts = server.Requests.GroupBy(r => r.DeviceToken).ToDictionary(g => g.Key, g => g.Count());
        foreach (var token in tokens)
        {
            counts[token].Should().Be(1, $"token {token} must be delivered exactly once");
        }
    }

    /// <summary>
    /// Fails the first <paramref name="failures"/> dials the way a refused connect does, inside the connect
    /// callback and after the permit was taken, so a stranded permit would be visible as a permanently shrunken
    /// pool.
    /// </summary>
    private sealed class DialRefuser(int failures)
    {
        private int _dials;

        public int FailedDials { get; private set; }

        public async Task GateAsync()
        {
            if (Interlocked.Increment(ref _dials) <= failures)
            {
                FailedDials++;
                await Task.Delay(10, AbortToken);

                throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused.");
            }
        }
    }
}
