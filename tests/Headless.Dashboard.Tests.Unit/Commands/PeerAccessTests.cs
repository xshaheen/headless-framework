// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Time.Testing;
using Tests.Authentication;
using static Tests.Commands.CommandTestHost;

namespace Tests.Commands;

/// <summary>
/// Calls from a module's peers: the module's own scheme admits them on its API, command, and Operation endpoints
/// under the module's peer policy alone, and on nothing else.
/// </summary>
public sealed class PeerAccessTests : TestBase
{
    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();
    private readonly FakeRunStore _store = new();

    public PeerAccessTests()
    {
        _store.SeedFailed(3);
    }

    [Fact]
    public async Task should_admit_a_trusted_peer_on_the_module_api_without_a_session()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        using var response = await _SendAsync(client, HttpMethod.Get, "/headless/api/diag/items", TrustedPeer);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Be("items");
    }

    [Fact]
    public async Task should_run_a_command_and_an_operation_for_a_trusted_peer_whom_no_user_permission_covers()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        // The operate policy, which a peer principal never passes, is skipped: the peer policy authorized the call.
        using var run = await _SendAsync(
            client,
            HttpMethod.Post,
            $"{Base}/{FakeCommandModule.Requeue}",
            TrustedPeer,
            new
            {
                ids = new[] { "run-000001" },
                reason = "forwarded",
                confirmed = true,
            }
        );
        using var confirm = await _SendAsync(
            client,
            HttpMethod.Post,
            $"{Base}/{FakeCommandModule.Requeue}/confirm",
            TrustedPeer,
            new { filter = new { queue = "default" }, reason = "forwarded" }
        );

        run.StatusCode.Should().Be(HttpStatusCode.OK);
        Module(app).Executed.Should().ContainSingle();
        _store.Get("run-000001").State.Should().NotBe(FakeRunState.Failed);
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(confirm, AbortToken)).GetProperty("token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_refuse_a_peer_the_peer_policy_refuses_with_403()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        using var api = await _SendAsync(client, HttpMethod.Get, "/headless/api/diag/items", "node-x");
        using var commands = await _SendAsync(client, HttpMethod.Get, Base, "node-x");

        api.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        commands.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_answer_401_to_a_call_with_neither_a_session_nor_a_peer_key()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        using var api = await _SendAsync(client, HttpMethod.Get, "/headless/api/diag/items", peer: null);
        using var commands = await _SendAsync(client, HttpMethod.Get, Base, peer: null);

        api.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        commands.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_admit_a_trusted_peer_on_no_other_module_and_none_of_the_shell_endpoints()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        using var otherModule = await _SendAsync(client, HttpMethod.Get, "/headless/api/audit/whoami", TrustedPeer);
        using var shell = await _SendAsync(client, HttpMethod.Get, "/headless/api/needs-you", TrustedPeer);

        otherModule.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        shell.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_keep_a_signed_in_viewer_to_the_users_permissions_on_a_module_that_admits_peers()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, "Bearer diag-viewer", AbortToken);

        using var run = await PostAsync(
            client,
            $"{Base}/{FakeCommandModule.Requeue}",
            new
            {
                ids = new[] { "run-000001" },
                reason = "stuck",
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        run.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Module(app).Executed.Should().BeEmpty();
    }

    private Task<WebApplication> _StartAsync()
    {
        return CommandTestHost.StartAsync(
            new CommandTestHostOptions
            {
                Store = _store,
                Time = _time,
                ConfigureDashboard = dashboard =>
                    dashboard.AddModule<AuditEndpointModule, DashboardModuleOptions>("audit"),
            },
            AbortToken
        );
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? peer,
        object? body = null
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        if (peer is not null)
        {
            request.Headers.TryAddWithoutValidation(PeerKeyHandler.Header, peer);
        }

        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        }

        return await client.SendAsync(request, AbortToken);
    }
}
