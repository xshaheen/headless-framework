// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Authentication;

namespace Tests.Commands;

/// <summary>
/// A host serving the dashboard under host authentication with <see cref="FakeCommandModule"/> as <c>diag</c>.
/// <c>Bearer ops</c> and <c>Bearer diag-operator</c> may operate it while the gate allows, <c>Bearer diag-viewer</c> may
/// only view it, and the purge capability is a host policy of its own name.
/// </summary>
internal static class CommandTestHost
{
    public const string Base = "/headless/api/diag/commands";
    public const string LiveOperatorsPolicy = "DiagLiveOperators";

    /// <summary>The peer the module's peer policy trusts; any other peer key signs in a peer it refuses.</summary>
    public const string TrustedPeer = "node-a";

    public static Task<WebApplication> StartAsync(CommandTestHostOptions options, CancellationToken cancellationToken)
    {
        return ShellTestHost.StartAsync(
            dashboard =>
            {
                dashboard
                    .WithHostAuthentication(host =>
                    {
                        host.ViewPolicy = ShellTestHost.OpsPolicy;
                        // Long enough that the Operation token, not the session, is what expires in these tests.
                        host.SessionIdleTimeout = TimeSpan.FromHours(4);
                    })
                    .AddModule<FakeCommandModule, FakeCommandModuleOptions>("diag");

                options.ConfigureDashboard?.Invoke(dashboard);

                if (options.DisabledIds.Length > 0)
                {
                    dashboard.Contributions(c => c.Disable(options.DisabledIds));
                }
            },
            new ShellTestHostOptions
            {
                Time = options.Time,
                DataProtection = options.DataProtection,
                ConfigureServices = services =>
                {
                    services.AddSingleton(options.Store);
                    services.AddSingleton<IDashboardOperatorLog>(options.Log);
                    services
                        .AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, PeerKeyHandler>(PeerKeyHandler.SchemeName, _ => { });
                    services
                        .AddAuthorizationBuilder()
                        .AddPolicy(
                            LiveOperatorsPolicy,
                            policy => policy.RequireAssertion(context => options.Gate.Operate && _IsOperator(context))
                        )
                        .AddPolicy(
                            FakeCommandModule.PeersPolicy,
                            policy => policy.RequireClaim(PeerKeyHandler.PeerClaim, TrustedPeer)
                        )
                        .AddPolicy(
                            FakeCommandModule.Purge,
                            policy => policy.RequireAssertion(context => options.Gate.Purge && _IsOperator(context))
                        );
                    services.Configure<FakeCommandModuleOptions>(module =>
                    {
                        module.ViewPolicy = ShellTestHost.DiagViewersPolicy;
                        module.OperatePolicy = LiveOperatorsPolicy;
                    });
                    DashboardCommandsSetup.AddCommands(services);
                    options.ConfigureServices?.Invoke(services);
                },
                MapHost = app =>
                {
                    var registration = app.Services.GetRequiredService<DashboardRegistration>();

                    // The shell maps the command endpoints itself once its setup wires them; map them here until then.
                    if (!registration.EndpointMappers.Contains(DashboardCommandsSetup.MapCommands))
                    {
                        DashboardCommandsSetup.MapCommands(registration.Group!);
                    }
                },
            },
            cancellationToken
        );
    }

    public static FakeCommandModule Module(WebApplication app) => app.Services.GetRequiredService<FakeCommandModule>();

    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        object body,
        string? cookie,
        CancellationToken cancellationToken,
        bool ui = true,
        bool requestedWith = true
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        _AddHeaders(request, cookie, ui, requestedWith);

        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string path,
        string? cookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        _AddHeaders(request, cookie, ui: true);

        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.Clone();
    }

    private static void _AddHeaders(HttpRequestMessage request, string? cookie, bool ui, bool requestedWith = true)
    {
        if (requestedWith)
        {
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (ui)
        {
            request.Headers.TryAddWithoutValidation("X-Headless-Dashboard-Channel", "ui");
        }
    }

    private static bool _IsOperator(AuthorizationHandlerContext context)
    {
        return context.User.IsInRole("ops") || context.User.IsInRole("diag-operator");
    }
}

/// <summary>Settings for one <see cref="CommandTestHost"/> node.</summary>
internal sealed class CommandTestHostOptions
{
    public required FakeRunStore Store { get; init; }

    public required TimeProvider Time { get; init; }

    public PermissionGate Gate { get; init; } = new();

    public RecordingOperatorLog Log { get; init; } = new();

    /// <summary>Shared by two nodes that serve chunks of one Operation, as nodes sharing one key ring do.</summary>
    public IDataProtectionProvider? DataProtection { get; init; }

    public string[] DisabledIds { get; init; } = [];

    public Action<IServiceCollection>? ConfigureServices { get; init; }

    public Action<HeadlessDashboardSetupBuilder>? ConfigureDashboard { get; init; }
}

/// <summary>
/// Signs a peer call in from <c>X-Peer-Key: &lt;node&gt;</c> as that node, standing in for the shared-key scheme a
/// module declares for node-to-node calls; without the header the request gets no result.
/// </summary>
internal sealed class PeerKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DiagPeer";
    public const string Header = "X-Peer-Key";
    public const string PeerClaim = "peer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var node = Request.Headers[Header].ToString();

        if (node.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new(ClaimTypes.Name, "peer:" + node), new(PeerClaim, node)], SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), SchemeName)));
    }
}
