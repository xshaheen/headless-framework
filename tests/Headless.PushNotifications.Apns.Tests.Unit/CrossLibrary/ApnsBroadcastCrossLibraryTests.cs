// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using Headless.PushNotifications.Apns;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.CrossLibrary;

/// <summary>
/// Compares our iOS 18 broadcast and channel-management requests with the ones @parse/node-apn sends, the only
/// surveyed APNs client that implements them. The fixture comes from <c>make apns-oracles</c>.
/// </summary>
public sealed class ApnsBroadcastCrossLibraryTests : TestBase
{
    private static readonly Lazy<JsonObject> _Fixture = new(() =>
        JsonNode
            .Parse(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "CrossLibrary", "Fixtures", "node-apn-broadcast.json")
                )
            )!
            .AsObject()
    );

    private static readonly Lazy<JsonObject> _Scenario = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrossLibrary", "scenarios.json")))![
            "broadcast"
        ]!.AsObject()
    );

    private FakeApnsServer _server = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _server = await FakeApnsServer.StartAsync(AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _server.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_send_the_broadcast_node_apn_sends()
    {
        // given - the shared scenario's update at its own timestamp, with our defaults (priority 5, expiration 0),
        // which the generator sets explicitly on node-apn.
        var aps = _Scenario.Value["update"]!["aps"]!;
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(aps["timestamp"]!.GetValue<long>()));
        await using var provider = _server.CreateProvider(configureServices: s => s.AddSingleton<TimeProvider>(clock));
        var service = provider.GetRequiredService<IApnsPushNotificationService>();
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = JsonSerializer.SerializeToElement(aps["content-state"]),
        };

        // when
        var result = await service.SendBroadcastAsync(_String(_Scenario.Value, "channelId"), notification, AbortToken);

        // then
        result.IsSucceeded.Should().BeTrue();
        _AssertSameRequest(_Fixture.Value["broadcast"]!.AsObject(), _server.Requests.Single(), expectedBody: true);
    }

    [Fact]
    public async Task should_send_the_channel_requests_node_apn_sends_except_the_listed_differences()
    {
        // given
        var channelId = _String(_Scenario.Value, "channelId");
        _server.Responder = request =>
            request.Method switch
            {
                "POST" => new FakeApnsReply(
                    201,
                    Headers: new Dictionary<string, string>(StringComparer.Ordinal) { ["apns-channel-id"] = channelId }
                ),
                "DELETE" => new FakeApnsReply(204),
                _ when request.Path.EndsWith("/all-channels", StringComparison.Ordinal) => new FakeApnsReply(
                    200,
                    RawBody: """{"channels":[]}"""
                ),
                _ => new FakeApnsReply(200, RawBody: """{"message-storage-policy":1,"push-type":"LiveActivity"}"""),
            };
        await using var provider = _server.CreateProvider();
        var channels = provider.GetRequiredService<IApnsBroadcastChannelService>();

        // when
        await channels.CreateAsync(ApnsChannelStoragePolicy.MostRecentMessageStored, AbortToken);
        await channels.GetAsync(channelId, AbortToken);
        await channels.ListAsync(AbortToken);
        await channels.DeleteAsync(channelId, AbortToken);

        // then
        var sent = _server.Requests.ToArray();
        var expected = _Fixture.Value["channels"]!.AsObject();

        foreach (var (operation, index) in new[] { ("create", 0), ("read", 1), ("list", 2), ("delete", 3) })
        {
            var fixture = expected[operation]!.AsObject();

            // node-apn sends apns-priority 10 on every channel request (its removeNonChannelRelatedProperties sets
            // it); Apple's channel-management header table has no apns-priority, so we send none.
            fixture["headers"]!["apns-priority"]!.GetValue<string>().Should().Be("10");
            fixture["headers"]!.AsObject()["apns-priority"] = null;

            if (operation == "create")
            {
                // node-apn writes "push-type": "liveactivity"; Apple's create body table says "Allowed value is
                // LiveActivity" and its sample sends "LiveActivity", which we follow.
                fixture["body"]!["push-type"]!.GetValue<string>().Should().Be("liveactivity");
                fixture["body"]!.AsObject()["push-type"] = "LiveActivity";
            }

            _AssertSameRequest(fixture, sent[index], expectedBody: operation == "create");
        }
    }

    [Fact]
    public void should_record_the_broadcast_fixture_with_the_pinned_node_apn_version()
    {
        var pinned = JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrossLibrary", "Pins", "package.json"))
        )!["dependencies"]!["@parse/node-apn"]!.GetValue<string>();

        _String(_Fixture.Value, "version")
            .Should()
            .Be(pinned, "the broadcast fixture must be regenerated with `make apns-oracles` after a pin change");
    }

    private static void _AssertSameRequest(JsonObject fixture, FakeApnsRequest sent, bool expectedBody)
    {
        sent.Method.Should().Be(_String(fixture, "method"));
        sent.Path.Should().Be(_String(fixture, "path"));
        sent.Headers.Should().ContainKey("apns-request-id", "node-apn and our provider both send an apns-request-id");

        foreach (var (name, value) in fixture["headers"]!.AsObject())
        {
            var ours = sent.Headers.TryGetValue(name, out var header) ? header : null;
            ours.Should().Be(value?.GetValue<string>(), $"header {name} should match node-apn");
        }

        if (!expectedBody)
        {
            sent.Body.Should().BeEmpty();
            fixture["body"].Should().BeNull();

            return;
        }

        JsonNode.DeepEquals(JsonNode.Parse(sent.Body), fixture["body"]).Should().BeTrue(sent.Body);
    }

    private static string _String(JsonNode node, string property) => node[property]!.GetValue<string>();
}
