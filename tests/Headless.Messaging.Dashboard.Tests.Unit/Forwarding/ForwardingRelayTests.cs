// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Headless.Dashboard;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// What a read relayed to another node may carry: the path stays inside the peer's Messaging API, a redirect is never
/// followed, the peer's answer is served as JSON, and an oversized answer is not read.
/// </summary>
public sealed class ForwardingRelayTests : TestBase
{
    private const string _Api = "/headless/api/messaging";
    private const string _PeerApi = _Api + "/";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discovery = new();
    private readonly RecordingOperatorLog _log = new();

    public ForwardingRelayTests()
    {
        _discovery.Set(ForwardingHost.Entry("node-b", "https://node-b"));
    }

    [Theory]
    [InlineData("a%5C..%5C..%5C..%5C..%5C..%5C..%5Cinternal%5Cmetrics")]
    [InlineData("a%5C..%5C..%5Cpeer%5Cattention")]
    [InlineData("id%3Fnode=node-c")]
    [InlineData("id%23fragment")]
    [InlineData("id%0Aline")]
    public async Task should_answer_400_and_call_no_node_when_a_relayed_path_segment_could_leave_the_api(string id)
    {
        // given
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/messages/published/{id}?node=node-b",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await MessagingCommandHost.ReadAsync(response, AbortToken);
        problem
            .GetProperty("rules")
            .GetProperty(NodeRelayFilter.PathField)
            .GetString()
            .Should()
            .Be(DashboardValidation.InvalidRule);
        _network.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("%2E%2E")]
    [InlineData("%2E")]
    public async Task should_call_no_node_when_an_id_is_an_escaped_dot_segment(string id)
    {
        // given
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/messages/published/{id}?node=node-b",
            cookie,
            AbortToken
        );

        // then: the dot segment is removed before routing, so no relayed route matches
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _network.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("..%2F..%2F..%2F..%2Finternal%2Fmetrics")]
    [InlineData("..%2F..%2Fpeer%2Fattention")]
    [InlineData("a%252E%252E")]
    public async Task should_relay_an_id_with_escaped_slashes_as_one_segment_inside_the_api(string id)
    {
        // given
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/messages/published/{id}?node=node-b",
            cookie,
            AbortToken
        );

        // then: the whole id is one segment of the message route on the peer
        var call = _network.Calls.Should().ContainSingle().Subject;
        call.Uri.AbsolutePath.Should().StartWith(_PeerApi + "messages/published/");
        call.Uri.AbsolutePath[(_PeerApi + "messages/published/").Length..].Should().NotContain("/");
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_relay_a_message_read_to_node_b_when_its_id_names_node_b()
    {
        // given
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);
        const string id = "node-b~primary~0192f3c1-7a1b-7c3d-9e4f-0a1b2c3d4e5f";
        _network.Answer("node-b", _ => _Json(HttpStatusCode.OK, """{"row":{}}"""));

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/messages/published/{id}",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _network.Calls.Should().ContainSingle().Subject;
        call.Uri.Should().Be(new Uri($"https://node-b{_Api}/messages/published/{id}"));
        call.Signed.Should().BeTrue();
        call.CarriedRawKey.Should().BeFalse();
    }

    [Theory]
    [InlineData("/../../../internal/metrics")]
    [InlineData("/messages/published/a\\..\\..\\..\\..\\internal")]
    [InlineData("/peer/attention")]
    [InlineData("/PEER/attention")]
    [InlineData("/peer")]
    [InlineData("/messages/../peer/attention")]
    public async Task should_refuse_and_send_nothing_when_a_relayed_path_would_leave_the_api_or_reach_the_peer_endpoints(
        string path
    )
    {
        // given
        await using var nodeA = await _StartAAsync();
        var forwarding = nodeA.Services.GetRequiredService<MessagingForwarding>();
        var peer = await forwarding.Directory.FindAsync("node-b", AbortToken);

        // when
        var relay = () => forwarding.Client.RelayAsync(peer!, path, new ClaimsPrincipal(), AbortToken);

        // then
        await relay.Should().ThrowAsync<InvalidOperationException>();
        _network.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a\\..\\..", null)]
    [InlineData("..", null)]
    [InlineData("a/./b", null)]
    [InlineData("a?b", null)]
    [InlineData("a#b", null)]
    [InlineData("a\u0000b", null)]
    [InlineData("a\u007Fb", null)]
    [InlineData("/messages/published/node-b~primary~1", "/messages/published/node-b~primary~1")]
    [InlineData("/messages/published/a b:c", "/messages/published/a%20b%3Ac")]
    [InlineData("/messages/published/a%2Fb", "/messages/published/a%252Fb")]
    [InlineData("/", "/")]
    public void should_escape_each_segment_or_refuse_the_path(string path, string? expected)
    {
        NodeRelayFilter.EscapePath(path).Should().Be(expected);
    }

    [Fact]
    public async Task should_report_node_b_as_not_answering_and_follow_no_redirect_when_node_b_answers_302()
    {
        // given
        _network.Answer(
            "node-b",
            _ =>
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://node-x/stolen");

                return redirect;
            }
        );
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
        response.Headers.GetValues(ForwardingProtocol.NodeStateHeader).Should().Equal(PeerState.NotAnswering.ToWire());
        _network.Calls.Should().ContainSingle().Which.Uri.Host.Should().Be("node-b");
    }

    [Fact]
    public async Task should_send_the_signed_call_to_no_other_host_when_a_peer_on_the_network_redirects()
    {
        // given: a real peer on loopback that redirects every call to another listener, reached through the
        // forwarding client's own primary handler rather than the test network
        var stolen = new List<string?>();
        await using var thief = await _ListenAsync(app =>
            app.Run(context =>
            {
                lock (stolen)
                {
                    stolen.Add(context.Request.Headers[ForwardingProtocol.SignatureHeader].ToString());
                }

                return Task.CompletedTask;
            })
        );
        var thiefAddress = _AddressOf(thief);
        await using var redirecting = await _ListenAsync(app =>
            app.Run(context =>
            {
                context.Response.Redirect(thiefAddress + context.Request.Path);

                return Task.CompletedTask;
            })
        );
        _discovery.Set(ForwardingHost.Entry("node-b", _AddressOf(redirecting)));
        await using var nodeA = await MessagingCommandHost.StartAsync(
            _time,
            _log,
            AbortToken,
            "node-a",
            configure: services =>
            {
                services.AddSingleton<INodeDiscoveryProvider>(_discovery);
                services.PostConfigure<MessagingDashboardOptions>(module =>
                {
                    module.Forwarding.Key = ForwardingHost.Key;
                    module.Forwarding.AllowInsecurePeers = true;
                });
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
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Headers.GetValues(ForwardingProtocol.NodeStateHeader).Should().Equal(PeerState.NotAnswering.ToWire());

        lock (stolen)
        {
            stolen.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "application/json")]
    [InlineData(HttpStatusCode.NotFound, "application/problem+json")]
    [InlineData(HttpStatusCode.InternalServerError, "application/problem+json")]
    public async Task should_serve_a_relayed_answer_as_json_with_nosniff_whatever_content_type_the_peer_sent(
        HttpStatusCode status,
        string expected
    )
    {
        // given
        const string body = "<script>alert(document.cookie)</script>";
        _network.Answer(
            "node-b",
            _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/html") }
        );
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
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be(expected);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be(body);
    }

    [Fact]
    public async Task should_report_node_b_as_not_answering_when_its_answer_is_larger_than_the_cap()
    {
        // given
        _network.Answer(
            "node-b",
            _ =>
                _Json(
                    HttpStatusCode.OK,
                    "\"" + new string('x', MessagingForwardingOptions.DefaultMaxResponseBytes) + "\""
                )
        );
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
        response.Headers.GetValues(ForwardingProtocol.NodeStateHeader).Should().Equal(PeerState.NotAnswering.ToWire());
    }

    [Theory]
    [InlineData(1024, HttpStatusCode.OK)]
    [InlineData(1023, HttpStatusCode.BadGateway)]
    public async Task should_read_a_peer_answer_only_up_to_the_configured_cap(int cap, HttpStatusCode expected)
    {
        // given: a 1024-byte answer
        _network.Answer("node-b", _ => _Json(HttpStatusCode.OK, "\"" + new string('x', 1022) + "\""));
        await using var nodeA = await _StartAAsync(options => options.Forwarding.MaxResponseBytes = cap);
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
        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task should_refuse_to_start_when_the_response_cap_is_not_positive(int cap)
    {
        // when
        var start = () => _StartAAsync(options => options.Forwarding.MaxResponseBytes = cap);

        // then
        var failure = await start.Should().ThrowAsync<Exception>();
        failure.Which.ToString().Should().Contain("Forwarding.MaxResponseBytes must be greater than 0.");
    }

    private Task<WebApplication> _StartAAsync(Action<MessagingDashboardOptions>? options = null) =>
        ForwardingHost.StartAsync("node-a", _network, _discovery, _time, _log, AbortToken, options);

    private static HttpResponseMessage _Json(HttpStatusCode status, string json)
    {
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return new HttpResponseMessage(status) { Content = content };
    }

    private static async Task<WebApplication> _ListenAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        map(app);
        await app.StartAsync(AbortToken);

        return app;
    }

    private static string _AddressOf(WebApplication app) => app.Urls.Single().TrimEnd('/');
}
