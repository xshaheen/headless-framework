// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tests.Authentication;
using Tests.Modules.Fixtures;

namespace Tests.Streaming;

/// <summary>
/// The live stream on a real host: topic and permission filtering, redacted projections, resuming from the last event
/// id, closing when the session needs checking again, coalescing, heartbeats, and shutdown.
/// </summary>
public sealed class StreamEndpointTests : TestBase
{
    private const string _ShellStream = "/headless/api/stream";

    private static readonly TimeSpan _Coalesce = DashboardEventPublisher.CoalesceWindow;

    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();
    private readonly FakeChangeStore _store;
    private volatile bool _hostStillValid = true;

    public StreamEndpointTests()
    {
        _store = new FakeChangeStore(_time);
    }

    [Fact]
    public async Task should_send_a_change_of_a_subscribed_topic_and_not_one_of_another_topic()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);

        var subscribed = await stream.NextAsync(AbortToken);
        subscribed!.Type.Should().Be("subscribed");
        _Strings(subscribed.Json.GetProperty("topics")).Should().Equal("diag.run");

        var publisher = app.Services.GetRequiredService<IDashboardEventPublisher>();
        publisher.Publish(FakeChangeStore.Change("diag", "other", "o-1", "1"));
        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-1", "1"));
        _time.Advance(_Coalesce);

        var change = await FakeClock.WithinAsync(stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        change.Type.Should().Be("change");
        change.Json.GetProperty("topic").GetString().Should().Be("diag.run");
        change.Json.GetProperty("id").GetString().Should().Be("r-1");
    }

    [Fact]
    public async Task should_send_no_event_of_a_module_the_user_may_not_view_even_when_the_stream_names_its_topic()
    {
        // Module view policies apply under host authentication; WithNoAuth opens every module to everyone.
        await using var app = await ShellTestHost.StartAsync(
            dashboard =>
                dashboard
                    .WithHostAuthentication()
                    .AddModule<DiagAStreamModule, DiagAStreamModuleOptions>("diag-a")
                    .AddModule<DiagBStreamModule, DiagBStreamModuleOptions>("diag-b"),
            new ShellTestHostOptions
            {
                Time = _time,
                ConfigureServices = services =>
                {
                    services.AddSingleton(_store);
                    services.Configure<DiagBStreamModuleOptions>(o => o.ViewPolicy = ShellTestHost.DiagViewersPolicy);
                    StreamingWiring.AddServices(services);
                },
                MapHost = StreamingWiring.MapEndpoints,
            },
            AbortToken
        );
        using var client = ShellTestHost.Client(app);
        var path = _ShellStream + "?topics=" + Uri.EscapeDataString("diag-a.run,diag-b.run");
        var outsiderCookie = await ShellTestHost.SignInAsync(client, "Bearer outsider", AbortToken);
        var viewerCookie = await ShellTestHost.SignInAsync(client, "Bearer diag-viewer", AbortToken);
        await using var outsider = await SseStream.OpenAsync(client, path, AbortToken, _Cookie(outsiderCookie));
        await using var viewer = await SseStream.OpenAsync(client, path, AbortToken, _Cookie(viewerCookie));

        var outsiderTopics = (await outsider.NextAsync(AbortToken))!.Json.GetProperty("topics");
        var viewerTopics = (await viewer.NextAsync(AbortToken))!.Json.GetProperty("topics");
        _Strings(outsiderTopics).Should().Equal("diag-a.run");
        _Strings(viewerTopics).Should().Equal("diag-a.run", "diag-b.run");

        var publisher = app.Services.GetRequiredService<IDashboardEventPublisher>();
        publisher.Publish(FakeChangeStore.Change("diag-b", "run", "b-1", "1") with { AffectsAttention = true });
        publisher.Publish(FakeChangeStore.Change("diag-a", "run", "a-1", "1"));
        _time.Advance(_Coalesce);

        var outsiderEvent = await FakeClock.WithinAsync(outsider.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        var viewerEvent = await FakeClock.WithinAsync(viewer.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        outsiderEvent.Type.Should().Be("change");
        outsiderEvent.Json.GetProperty("module").GetString().Should().Be("diag-a");
        viewerEvent.Type.Should().Be("change");
        viewerEvent.Json.GetProperty("module").GetString().Should().Be("diag-b");
    }

    [Fact]
    public async Task should_send_a_projection_masked_by_the_module_formatter()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);

        app.Services.GetRequiredService<IDashboardEventPublisher>()
            .Publish(FakeChangeStore.Change("diag", "run", "r-1", "1", """{"user":"ada","password":"hunter2"}"""));
        _time.Advance(_Coalesce);

        var change = await FakeClock.WithinAsync(stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        var projection = change.Json.GetProperty("projection");
        projection.GetProperty("user").GetString().Should().Be("ada");
        projection.GetProperty("password").GetString().Should().Be("***");
        change.Data.Should().NotContain("hunter2");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_replay_exactly_the_missed_events_in_order_when_reconnecting_inside_the_buffer(
        bool lastEventIdInHeader
    )
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        var publisher = app.Services.GetRequiredService<IDashboardEventPublisher>();

        // Another browser keeps its stream open, so the change readers never stop and the node records no gap.
        await using var keeper = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        var lastSeen = await _ReceiveOneAsync(client, publisher, "r-1");

        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-2", "1"));
        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-3", "1"));
        _time.Advance(_Coalesce);

        await using var reconnected = lastEventIdInHeader
            ? await SseStream.OpenAsync(
                client,
                StreamTestHost.StreamPath("diag.run"),
                AbortToken,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Last-Event-ID"] = lastSeen }
            )
            : await SseStream.OpenAsync(
                client,
                StreamTestHost.StreamPath("diag.run") + "&lastEventId=" + Uri.EscapeDataString(lastSeen),
                AbortToken
            );

        (await reconnected.NextAsync(AbortToken))!.Type.Should().Be("subscribed");
        var replayed = new[]
        {
            await FakeClock.WithinAsync(reconnected.NextExceptHeartbeatAsync(AbortToken), AbortToken),
            await FakeClock.WithinAsync(reconnected.NextExceptHeartbeatAsync(AbortToken), AbortToken),
        };
        replayed.Select(e => e.Type).Should().Equal("change", "change");
        replayed.Select(e => e.Json.GetProperty("id").GetString()).Should().Equal("r-2", "r-3");

        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-4", "1"));
        _time.Advance(_Coalesce);
        var live = await FakeClock.WithinAsync(reconnected.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        live.Json.GetProperty("id").GetString().Should().Be("r-4");
    }

    [Fact]
    public async Task should_send_one_reset_then_live_events_when_the_last_event_id_names_another_node()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();

        await using var stream = await SseStream.OpenAsync(
            client,
            StreamTestHost.StreamPath("diag.run"),
            AbortToken,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Last-Event-ID"] = "another-node:7" }
        );

        (await stream.NextAsync(AbortToken))!.Type.Should().Be("subscribed");
        (await stream.NextAsync(AbortToken))!.Type.Should().Be("reset");

        app.Services.GetRequiredService<IDashboardEventPublisher>()
            .Publish(FakeChangeStore.Change("diag", "run", "r-1", "1"));
        _time.Advance(_Coalesce);

        var live = await FakeClock.WithinAsync(stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        live.Type.Should().Be("change");
        live.Json.GetProperty("id").GetString().Should().Be("r-1");
    }

    [Fact]
    public async Task should_send_one_reset_then_live_events_when_the_last_event_id_is_older_than_the_buffer()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        var publisher = app.Services.GetRequiredService<IDashboardEventPublisher>();

        // The keeper holds the readers open, so the reset can only come from the ring having overwritten the id.
        await using var keeper = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        var lastSeen = await _ReceiveOneAsync(client, publisher, "r-0");

        for (var i = 1; i <= EventRingBuffer.DefaultCapacity + 10; i++)
        {
            publisher.Publish(FakeChangeStore.Change("diag", "other", $"o-{i}", "1"));
        }

        _time.Advance(_Coalesce);

        await using var reconnected = await SseStream.OpenAsync(
            client,
            StreamTestHost.StreamPath("diag.run"),
            AbortToken,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Last-Event-ID"] = lastSeen }
        );

        (await reconnected.NextAsync(AbortToken))!.Type.Should().Be("subscribed");
        (await reconnected.NextAsync(AbortToken))!.Type.Should().Be("reset");

        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-1", "1"));
        _time.Advance(_Coalesce);
        var live = await FakeClock.WithinAsync(reconnected.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        live.Json.GetProperty("id").GetString().Should().Be("r-1");
    }

    [Fact]
    public async Task should_refuse_a_cross_origin_request_with_a_message_naming_forwarded_headers()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();

        await using var refused = await SseStream.SendAsync(
            client,
            StreamTestHost.StreamPath("diag.run"),
            AbortToken,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Origin"] = "https://evil.example" }
        );
        await using var sameOrigin = await SseStream.SendAsync(
            client,
            StreamTestHost.StreamPath("diag.run"),
            AbortToken,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Origin"] = "http://localhost" }
        );

        refused.Response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var message = await refused.Response.Content.ReadAsStringAsync(AbortToken);
        message.Should().Contain("forwarded headers").And.Contain("https://evil.example");
        sameOrigin.Response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_close_the_stream_when_the_ticket_expires_and_refuse_the_cookie_afterwards()
    {
        await using var app = await _StartShellAsync(dashboard =>
            dashboard.WithHostAuthentication(host => host.SessionIdleTimeout = TimeSpan.FromMinutes(10))
        );
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, "Bearer ops", AbortToken);
        await using var stream = await SseStream.OpenAsync(client, _ShellStream, AbortToken, _Cookie(cookie));
        (await stream.NextAsync(AbortToken))!.Type.Should().Be("subscribed");

        _time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        (await FakeClock.WithinAsync(stream.NextAsync(AbortToken), AbortToken))!.Type.Should().Be("heartbeat");

        _time.Advance(TimeSpan.FromSeconds(1));
        var rest = await FakeClock.WithinAsync(stream.ReadToEndAsync(AbortToken), AbortToken);
        rest.Should().OnlyContain(type => string.Equals(type, "heartbeat", StringComparison.Ordinal));

        _time.Advance(TimeSpan.FromSeconds(1));
        await using var reconnect = await SseStream.SendAsync(client, _ShellStream, AbortToken, _Cookie(cookie));
        reconnect.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_close_an_api_key_stream_within_five_minutes_and_refuse_its_reconnect_after_the_key_changes()
    {
        var keyRing = new EphemeralDataProtectionProvider();
        await using var issuing = await _StartShellAsync(dashboard => dashboard.WithApiKey("k1"), keyRing);
        await using var rotated = await _StartShellAsync(dashboard => dashboard.WithApiKey("k2"), keyRing);
        using var issuingClient = ShellTestHost.Client(issuing);
        using var rotatedClient = ShellTestHost.Client(rotated);
        var cookie = await ShellTestHost.SignInAsync(issuingClient, "Bearer k1", AbortToken);
        await using var stream = await SseStream.OpenAsync(issuingClient, _ShellStream, AbortToken, _Cookie(cookie));
        (await stream.NextAsync(AbortToken))!.Type.Should().Be("subscribed");

        _time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        (await FakeClock.WithinAsync(stream.NextAsync(AbortToken), AbortToken))!.Type.Should().Be("heartbeat");

        _time.Advance(TimeSpan.FromSeconds(1));
        var rest = await FakeClock.WithinAsync(stream.ReadToEndAsync(AbortToken), AbortToken);
        rest.Should().OnlyContain(type => string.Equals(type, "heartbeat", StringComparison.Ordinal));

        await using var reconnect = await SseStream.SendAsync(rotatedClient, _ShellStream, AbortToken, _Cookie(cookie));
        reconnect.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_close_the_stream_at_the_next_revalidation_and_refuse_the_reconnect_the_host_revokes()
    {
        await using var app = await _StartShellAsync(dashboard =>
            dashboard.WithHostAuthentication(host =>
            {
                host.RevalidationInterval = TimeSpan.FromMinutes(1);
                host.Revalidate = (_, _) => ValueTask.FromResult(_hostStillValid);
            })
        );
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, "Bearer ops", AbortToken);
        await using var stream = await SseStream.OpenAsync(client, _ShellStream, AbortToken, _Cookie(cookie));
        (await stream.NextAsync(AbortToken))!.Type.Should().Be("subscribed");

        _time.Advance(TimeSpan.FromSeconds(59));
        (await FakeClock.WithinAsync(stream.NextAsync(AbortToken), AbortToken))!.Type.Should().Be("heartbeat");

        _time.Advance(TimeSpan.FromSeconds(1));
        var rest = await FakeClock.WithinAsync(stream.ReadToEndAsync(AbortToken), AbortToken);
        rest.Should().OnlyContain(type => string.Equals(type, "heartbeat", StringComparison.Ordinal));

        _hostStillValid = false;
        await using var reconnect = await SseStream.SendAsync(client, _ShellStream, AbortToken, _Cookie(cookie));
        reconnect.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_send_three_changes_to_one_object_within_the_window_as_one_event_with_the_latest_version()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);
        var publisher = app.Services.GetRequiredService<IDashboardEventPublisher>();

        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-1", "1"));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-1", "2"));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-1", "3"));
        _time.Advance(TimeSpan.FromMilliseconds(50));

        var coalesced = await FakeClock.WithinAsync(stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        coalesced.Json.GetProperty("id").GetString().Should().Be("r-1");
        coalesced.Json.GetProperty("version").GetString().Should().Be("3");

        // The next event is the next object's, so the first two versions were never sent.
        publisher.Publish(FakeChangeStore.Change("diag", "run", "r-2", "1"));
        _time.Advance(_Coalesce);
        var next = await FakeClock.WithinAsync(stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        next.Json.GetProperty("id").GetString().Should().Be("r-2");
    }

    [Fact]
    public async Task should_send_a_heartbeat_every_two_seconds()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        var subscribed = await stream.NextAsync(AbortToken);

        _time.Advance(TimeSpan.FromSeconds(2));
        var first = await FakeClock.WithinAsync(stream.NextAsync(AbortToken), AbortToken);
        _time.Advance(TimeSpan.FromSeconds(2));
        var second = await FakeClock.WithinAsync(stream.NextAsync(AbortToken), AbortToken);

        first!.Type.Should().Be("heartbeat");
        second!.Type.Should().Be("heartbeat");
        first.Id.Should().Be(subscribed!.Id, "a heartbeat carries the stream's position so a reconnect resumes there");
    }

    [Fact]
    public async Task should_disable_proxy_buffering_and_end_the_stream_at_once_when_the_host_stops()
    {
        await using var app = await StreamTestHost.StartDiagAsync(_time, _store, AbortToken);
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);

        stream.Response.Headers.GetValues("X-Accel-Buffering").Should().Equal("no");
        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var ended = stream.ReadToEndAsync(AbortToken);
        await FakeClock.WithinAsync(app.StopAsync(AbortToken), AbortToken);

        (await FakeClock.WithinAsync(ended, AbortToken)).Should().BeEmpty();
    }

    // Opens a stream, receives one change, and closes the stream, returning the change's event id.
    private async Task<string> _ReceiveOneAsync(HttpClient client, IDashboardEventPublisher publisher, string id)
    {
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);

        publisher.Publish(FakeChangeStore.Change("diag", "run", id, "1"));
        _time.Advance(_Coalesce);
        var change = await FakeClock.WithinAsync(stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        change.Json.GetProperty("id").GetString().Should().Be(id);

        return change.Id!;
    }

    private Task<WebApplication> _StartShellAsync(
        Action<HeadlessDashboardSetupBuilder> chooseMode,
        IDataProtectionProvider? keyRing = null
    )
    {
        return ShellTestHost.StartAsync(
            dashboard =>
            {
                chooseMode(dashboard);
                dashboard.AddModule<DiagEndpointModule, DiagModuleOptions>("diag");
            },
            new ShellTestHostOptions
            {
                Time = _time,
                DataProtection = keyRing,
                ConfigureServices = StreamingWiring.AddServices,
                MapHost = StreamingWiring.MapEndpoints,
            },
            AbortToken
        );
    }

    private static Dictionary<string, string> _Cookie(string cookie)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal) { ["Cookie"] = cookie };
    }

    private static IEnumerable<string?> _Strings(JsonElement array)
    {
        return array.EnumerateArray().Select(static item => item.GetString());
    }
}
