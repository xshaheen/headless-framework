// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// Key mode admits a forwarded call by its signature: what a node signs, what a peer refuses, and the round trip through
/// the real signing handler and the forwarding scheme.
/// </summary>
public sealed class ForwardingSignatureTests : TestBase
{
    private const string _Api = "/headless/api/messaging";
    private const string _Scope = _Api + "/peer/operations/messaging.re-execute/scope";
    private const string _Chunk = _Api + "/peer/operations/messaging.re-execute/chunk";
    private const string _ChunkBody = """{"token":"not-a-token","reason":"incident 42"}""";

    private static readonly string _NewKey = new('n', MessagingForwardingOptions.MinimumKeyLength);
    private static readonly string _WrongKey = new('w', MessagingForwardingOptions.MinimumKeyLength);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly StaticDiscovery _discoveryB = new();
    private readonly RecordingOperatorLog _logA = new();
    private readonly RecordingOperatorLog _logB = new();

    public ForwardingSignatureTests()
    {
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
    }

    [Fact]
    public async Task should_admit_a_signed_read_when_the_signature_matches_the_key()
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var client = MessagingCommandHost.Client(nodeB);
        using var request = _Read();
        await ForwardingHost.SignAsync(request, _time, AbortToken);

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_admit_a_signed_call_and_hand_the_endpoint_its_whole_body_when_the_call_has_a_body()
    {
        // given
        await using var nodeB = await _StartBAsync(_Operate);
        using var client = MessagingCommandHost.Client(nodeB);
        using var request = _Write(_Chunk, _ChunkBody);
        await ForwardingHost.SignAsync(request, _time, AbortToken);

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then: the endpoint parsed the body the signature check had already hashed, and refused only its token
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await MessagingCommandHost.ReadAsync(response, AbortToken);
        problem.GetProperty("rules").GetProperty("token").GetString().Should().Be("invalid");
    }

    [Fact]
    public async Task should_admit_a_call_signed_with_either_key_when_the_previous_key_is_set()
    {
        // given
        await using var nodeB = await _StartBAsync(options =>
        {
            options.Forwarding.Key = _NewKey;
            options.Forwarding.PreviousKey = ForwardingHost.Key;
        });
        using var client = MessagingCommandHost.Client(nodeB);
        using var signedWithPrevious = _Read();
        await ForwardingHost.SignAsync(signedWithPrevious, _time, AbortToken, ForwardingHost.Key);
        using var signedWithCurrent = _Read();
        await ForwardingHost.SignAsync(signedWithCurrent, _time, AbortToken, _NewKey);

        // when
        using var previous = await client.SendAsync(signedWithPrevious, AbortToken);
        using var current = await client.SendAsync(signedWithCurrent, AbortToken);

        // then
        previous.StatusCode.Should().Be(HttpStatusCode.OK);
        current.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("path")]
    [InlineData("query")]
    [InlineData("actor")]
    [InlineData("access")]
    [InlineData("hop")]
    public async Task should_refuse_with_401_key_rejected_when_a_signed_part_of_the_call_changed_after_signing(
        string tampered
    )
    {
        // given
        await using var nodeB = await _StartBAsync(_Operate);
        using var client = MessagingCommandHost.Client(nodeB);
        using var request = _Write(_Scope, "not json");
        await ForwardingHost.SignAsync(request, _time, AbortToken);
        _Tamper(request, tampered);

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        await _ShouldBeRejectedAsync(response, request);
    }

    [Theory]
    [InlineData(-61)]
    [InlineData(61)]
    [InlineData(-3600)]
    public async Task should_refuse_with_401_key_rejected_when_the_timestamp_is_outside_the_clock_skew(int seconds)
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var client = MessagingCommandHost.Client(nodeB);
        using var request = _Read();
        await ForwardingHost.SignAsync(
            request,
            new FakeTimeProvider(_time.GetUtcNow().AddSeconds(seconds)),
            AbortToken
        );

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        await _ShouldBeRejectedAsync(response, request);
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(60)]
    public async Task should_admit_a_signed_call_when_its_clock_is_off_by_at_most_the_clock_skew(int seconds)
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var client = MessagingCommandHost.Client(nodeB);
        using var request = _Read();
        await ForwardingHost.SignAsync(
            request,
            new FakeTimeProvider(_time.GetUtcNow().AddSeconds(seconds)),
            AbortToken
        );

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_refuse_a_replay_with_401_key_rejected_when_the_same_signed_call_arrives_twice()
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var client = MessagingCommandHost.Client(nodeB);
        using var first = _Read();
        await ForwardingHost.SignAsync(first, _time, AbortToken);
        using var replay = _Read();
        replay.Headers.TryAddWithoutValidation(
            ForwardingProtocol.SignatureHeader,
            first.Headers.GetValues(ForwardingProtocol.SignatureHeader)
        );

        // when
        using var admitted = await client.SendAsync(first, AbortToken);
        using var replayed = await client.SendAsync(replay, AbortToken);

        // then
        admitted.StatusCode.Should().Be(HttpStatusCode.OK);
        await _ShouldBeRejectedAsync(replayed, replay);
    }

    [Fact]
    public async Task should_refuse_with_401_key_rejected_when_the_call_is_signed_with_another_key()
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var client = MessagingCommandHost.Client(nodeB);
        using var request = _Read();
        await ForwardingHost.SignAsync(request, _time, AbortToken, _WrongKey);

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        await _ShouldBeRejectedAsync(response, request);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("v1;ts=1;nonce=AAAAAAAAAAAAAAAAAAAAAA;sig=short")]
    [InlineData("v2;ts=1;nonce=AAAAAAAAAAAAAAAAAAAAAA;sig=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task should_refuse_with_401_key_rejected_when_the_signature_is_missing_or_malformed(string header)
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var client = MessagingCommandHost.Client(nodeB);
        using var request = _Read();

        if (header.Length > 0)
        {
            request.Headers.TryAddWithoutValidation(ForwardingProtocol.SignatureHeader, header);
        }

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        await _ShouldBeRejectedAsync(response, request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_refuse_with_401_key_rejected_when_the_call_carries_the_raw_key_header_of_older_builds(
        bool alsoSigned
    )
    {
        // given
        await using var nodeB = await _StartBAsync();
        using var client = MessagingCommandHost.Client(nodeB);
        using var request = _Read();
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.LegacyKeyHeader, ForwardingHost.Key);

        if (alsoSigned)
        {
            await ForwardingHost.SignAsync(request, _time, AbortToken);
        }

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        await _ShouldBeRejectedAsync(response, request);
    }

    [Fact]
    public async Task should_admit_a_forwarded_read_and_send_no_key_when_a_node_calls_its_peer_through_the_client()
    {
        // given
        await using var nodeB = await _StartBAsync();
        await using var nodeA = await _StartAAsync();
        var sent = new List<string>();
        _network.Answer(
            "node-b",
            request =>
            {
                lock (sent)
                {
                    sent.AddRange(request.Headers.SelectMany(static header => header.Value));
                }

                return null;
            }
        );
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/received?node=node-b",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _network.Calls.Should().ContainSingle().Subject;
        call.Signed.Should().BeTrue();
        call.CarriedRawKey.Should().BeFalse();

        lock (sent)
        {
            sent.Should().NotBeEmpty().And.NotContain(value => value.Contains(ForwardingHost.Key));
        }
    }

    // Each row is one moment of the three-roll rotation, with node A rolled ahead of node B and then behind it.
    [Theory]
    [InlineData("old", "new", "old", "-")] // first roll: A admits the new key, B not yet
    [InlineData("old", "-", "old", "new")]
    [InlineData("new", "old", "old", "new")] // second roll: A signs with the new key, B still with the old
    [InlineData("old", "new", "new", "old")]
    [InlineData("new", "-", "new", "old")] // third roll: A dropped the old key, B not yet
    [InlineData("new", "old", "new", "-")]
    public async Task should_keep_forwarding_during_a_rotation_when_the_nodes_are_rolled_one_at_a_time(
        string keyA,
        string previousKeyA,
        string keyB,
        string previousKeyB
    )
    {
        // given
        await using var nodeB = await _StartBAsync(options =>
        {
            options.Forwarding.Key = _KeyNamed(keyB);
            options.Forwarding.PreviousKey = _KeyNamed(previousKeyB);
        });
        await using var nodeA = await _StartAAsync(options =>
        {
            options.Forwarding.Key = _KeyNamed(keyA);
            options.Forwarding.PreviousKey = _KeyNamed(previousKeyA);
        });
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/received?node=node-b",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_show_node_b_as_key_rejected_when_node_a_signs_with_a_key_node_b_does_not_hold()
    {
        // given: node B finished the rotation and dropped the previous key, node A still signs with it
        await using var nodeB = await _StartBAsync(options => options.Forwarding.Key = _NewKey);
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/received?node=node-b",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Headers.GetValues(ForwardingProtocol.NodeStateHeader).Should().Equal(PeerState.KeyRejected.ToWire());
    }

    private static string? _KeyNamed(string name) =>
        name switch
        {
            "old" => ForwardingHost.Key,
            "new" => _NewKey,
            _ => null,
        };

    private static void _Operate(MessagingDashboardOptions options) =>
        options.ForwardedAccess = MessagingForwardedAccess.Operate;

    private static HttpRequestMessage _Read()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_Api + "/received", UriKind.Relative));
        _AddForwardedHeaders(request, ForwardingProtocol.AccessView);

        return request;
    }

    private static HttpRequestMessage _Write(string path, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        _AddForwardedHeaders(request, ForwardingProtocol.AccessOperate);

        return request;
    }

    private static void _AddForwardedHeaders(HttpRequestMessage request, string access)
    {
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.HopHeader, "node-a");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.ActorHeader, "ops");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.AccessHeader, access);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
    }

    private static void _Tamper(HttpRequestMessage request, string part)
    {
        switch (part)
        {
            case "body":
                request.Content = new StringContent("not json!", Encoding.UTF8, "application/json");
                break;
            case "path":
                request.RequestUri = new Uri(request.RequestUri!, _Chunk);
                break;
            case "query":
                request.RequestUri = new Uri(request.RequestUri!, _Scope + "?x=1");
                break;
            case "actor":
                _Replace(request, ForwardingProtocol.ActorHeader, "admin");
                break;
            case "access":
                _Replace(request, ForwardingProtocol.AccessHeader, ForwardingProtocol.AccessView);
                break;
            case "hop":
                _Replace(request, ForwardingProtocol.HopHeader, "node-c");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(part), part, "Not a signed part of the call.");
        }
    }

    private static void _Replace(HttpRequestMessage request, string header, string value)
    {
        request.Headers.Remove(header);
        request.Headers.TryAddWithoutValidation(header, value);
    }

    private async Task _ShouldBeRejectedAsync(HttpResponseMessage response, HttpRequestMessage request)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues(ForwardingProtocol.RefusalHeader).Should().Equal(ForwardingProtocol.RefusalKey);
        _logB.Entries.Should().BeEmpty();

        // The refusal never echoes a key or the signature the call sent.
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().NotContain(ForwardingHost.Key).And.NotContain(_NewKey);

        if (request.Headers.TryGetValues(ForwardingProtocol.SignatureHeader, out var signature))
        {
            var sent = signature.Single();

            if (sent.Length > 0)
            {
                body.Should().NotContain(sent);
            }
        }
    }

    private async Task<WebApplication> _StartBAsync(Action<MessagingDashboardOptions>? options = null)
    {
        var app = await ForwardingHost.StartAsync("node-b", _network, _discoveryB, _time, _logB, AbortToken, options);
        _network.Add("node-b", app);

        return app;
    }

    private Task<WebApplication> _StartAAsync(Action<MessagingDashboardOptions>? options = null) =>
        ForwardingHost.StartAsync("node-a", _network, _discoveryA, _time, _logA, AbortToken, options);
}
