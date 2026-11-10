// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// The configurations a forwarding host refuses to start with, and the dashboard-only host that reads every node
/// through forwarding.
/// </summary>
public sealed class ForwardingStartupTests : TestBase
{
    private static readonly string _ShortKey = new('s', 16);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discovery = new();
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_refuse_to_start_when_forwarding_runs_under_no_auth()
    {
        // when
        var start = () =>
            _StartBareAsync(
                dashboard =>
                    dashboard.WithNoAuth().AddMessaging(options => options.Forwarding.Key = ForwardingHost.Key),
                services =>
                    services.AddHeadlessMessaging(setup =>
                    {
                        setup.UseInMemory();
                        setup.UseInMemoryStorage();
                    })
            );

        // then
        var message = await _StartupFailureAsync(start);
        message.Should().Contain("cannot run under WithNoAuth()").And.NotContain(ForwardingHost.Key);
    }

    [Fact]
    public async Task should_refuse_to_start_and_never_quote_the_key_when_the_key_is_shorter_than_32_characters()
    {
        // when
        var start = () =>
            ForwardingHost.StartAsync(
                "node-a",
                _network,
                _discovery,
                _time,
                _log,
                AbortToken,
                options => options.Forwarding.Key = _ShortKey
            );

        // then
        var message = await _StartupFailureAsync(start);
        message.Should().Contain("32").And.NotContain(_ShortKey);
    }

    [Theory]
    [InlineData("Missing", "which the host does not register")]
    [InlineData("MessagingViewers", "is also a view or operate policy")]
    public async Task should_refuse_to_start_when_the_forwarding_policy_is_missing_or_a_dashboard_policy(
        string policy,
        string expected
    )
    {
        // when
        var start = () =>
            ForwardingHost.StartAsync(
                "node-a",
                _network,
                _discovery,
                _time,
                _log,
                AbortToken,
                options =>
                {
                    options.Forwarding.Key = null;
                    options.Forwarding.Policy = policy;
                }
            );

        // then
        (await _StartupFailureAsync(start))
            .Should()
            .Contain(expected);
    }

    [Fact]
    public async Task should_use_the_host_policy_and_never_the_key_handler_when_a_policy_is_set()
    {
        // given
        await using var node = await ForwardingHost.StartAsync(
            "node-a",
            _network,
            _discovery,
            _time,
            _log,
            AbortToken,
            options =>
            {
                options.Forwarding.Key = null;
                options.Forwarding.Policy = PeerIdentityHandler.PolicyName;
            },
            PeerIdentityHandler.AddTo
        );
        using var client = MessagingCommandHost.Client(node);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/headless/api/messaging/received", UriKind.Relative)
        );
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.HopHeader, "node-b");
        // The deployment's old shared key: with a policy set it admits nobody.
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.KeyHeader, ForwardingHost.Key);

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues(ForwardingProtocol.RefusalHeader).Should().Equal(ForwardingProtocol.RefusalPolicy);
    }

    [Fact]
    public async Task should_open_on_the_first_node_when_the_dashboard_only_host_reads_without_a_node()
    {
        // given
        _discovery.Set(ForwardingHost.Entry("node-b", "https://node-b"));
        var nodeLog = new RecordingOperatorLog();
        await using var nodeB = await ForwardingHost.StartAsync(
            "node-b",
            _network,
            new StaticDiscovery(),
            _time,
            nodeLog,
            AbortToken
        );
        _network.Add("node-b", nodeB);
        await using var dashboard = await _StartStandaloneAsync();
        using var client = MessagingCommandHost.Client(dashboard);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            "/headless/api/messaging/received",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var call = _network.Calls.Should().ContainSingle().Subject;
        call.Caller.Should().Be("dashboard");
        call.Uri.Should().Be(new Uri("https://node-b/headless/api/messaging/received"));
    }

    [Fact]
    public async Task should_say_no_node_is_reachable_with_the_discovery_state_when_discovery_lists_no_node()
    {
        // given
        await using var dashboard = await _StartStandaloneAsync();
        using var client = MessagingCommandHost.Client(dashboard);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            "/headless/api/messaging/received",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var detail = (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("detail").GetString();
        detail.Should().Be("No Messaging node reachable. Service discovery lists no other Messaging node.");
        _network.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_to_start_the_dashboard_only_host_when_forwarding_is_off()
    {
        // when
        var start = () =>
            _StartStandaloneAsync(options =>
            {
                options.Forwarding.Key = null;
                options.Forwarding.Policy = null;
            });

        // then
        (await _StartupFailureAsync(start))
            .Should()
            .Contain("needs forwarding");
    }

    private Task<WebApplication> _StartStandaloneAsync(Action<MessagingDashboardOptions>? configure = null) =>
        _StartBareAsync(
            dashboard =>
                dashboard
                    .WithHostAuthentication(host =>
                    {
                        host.ViewPolicy = "MessagingViewers";
                        host.OperatePolicy = "MessagingOperators";
                    })
                    .AddMessagingStandalone(options =>
                    {
                        options.NodeName = "dashboard";
                        options.Forwarding.Key = ForwardingHost.Key;
                        configure?.Invoke(options);
                    }),
            services =>
            {
                services.AddSingleton<INodeDiscoveryProvider>(_discovery);
                services
                    .AddHttpClient(ForwardingProtocol.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _network.ForCaller("dashboard"));
            }
        );

    /// <summary>A host with the test sign-in and the dashboard <paramref name="dashboard"/> builds, and no Messaging.</summary>
    private async Task<WebApplication> _StartBareAsync(
        Action<HeadlessDashboardSetupBuilder> dashboard,
        Action<IServiceCollection>? configure = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication(MessagingCommandHost.RoleBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, MessagingCommandHost.RoleBearerHandler>(
                MessagingCommandHost.RoleBearerHandler.SchemeName,
                _ => { }
            );
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("MessagingViewers", policy => policy.RequireRole("ops", "viewer"))
            .AddPolicy("MessagingOperators", policy => policy.RequireRole("ops"));
        builder.Services.AddHeadlessDashboard(dashboard);
        builder.Services.AddSingleton<IDashboardOperatorLog>(_log);
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        try
        {
            app.MapHeadlessDashboard();
            await app.StartAsync(AbortToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    /// <summary>Starts a host that must refuse to start and returns every reason it gave.</summary>
    private static async Task<string> _StartupFailureAsync(Func<Task<WebApplication>> start)
    {
        var failure = await start.Should().ThrowAsync<Exception>();

        return failure.Which switch
        {
            AggregateException aggregate => string.Join(
                " | ",
                aggregate.Flatten().InnerExceptions.Select(inner => inner.Message)
            ),
            OptionsValidationException options => string.Join(" | ", options.Failures),
            var other => other.Message,
        };
    }
}
