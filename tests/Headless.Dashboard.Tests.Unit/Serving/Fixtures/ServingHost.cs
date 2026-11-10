// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Tests.Serving.Fixtures;

/// <summary>A module whose build is the embedded <c>modules/diag</c> fixture: an entry, a route chunk, and a stylesheet.</summary>
internal sealed class ServingDiagModule : IDashboardModule
{
    public string Id => "diag";

    public string DisplayName => "Diagnostics";

    public DashboardModuleAssets Assets { get; } = new() { Files = ServingHost.ModuleFiles("diag") };

    public void MapEndpoints(DashboardModuleEndpoints endpoints) { }
}

/// <summary>A module built against dashboard SDK 2.0.0, which the shell refuses.</summary>
internal sealed class ServingLegacyModule : IDashboardModule
{
    public string Id => "legacy";

    public string DisplayName => "Legacy";

    public DashboardModuleAssets Assets { get; } = new() { Files = ServingHost.ModuleFiles("legacy") };

    public void MapEndpoints(DashboardModuleEndpoints endpoints) { }
}

internal sealed class ServingModuleOptions : DashboardModuleOptions;

/// <summary>A session reader that answers every request with one fixed session.</summary>
internal sealed class FixedSessionSnapshot(DashboardSessionSnapshot state) : IDashboardSessionSnapshot
{
    public ValueTask<DashboardSessionSnapshot> GetAsync(HttpContext context, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(state);
    }
}

/// <summary>
/// Starts a test host that serves the dashboard with the fixture shell build and the <c>diag</c> and <c>legacy</c>
/// fixture modules.
/// </summary>
internal static partial class ServingHost
{
    /// <summary>A session that may view both modules and operate <c>diag</c>.</summary>
    /// <summary>A request that carries no session.</summary>
    public static readonly DashboardSessionSnapshot SignedOut = new()
    {
        Authenticated = false,
        Name = null,
        CanOperate = false,
        Modules = new Dictionary<string, DashboardModuleAccess>(StringComparer.Ordinal),
    };

    public static readonly DashboardSessionSnapshot Operator = Session(
        "operator",
        ("diag", new DashboardModuleAccess { CanView = true, CanOperate = true }),
        ("legacy", new DashboardModuleAccess { CanView = true, CanOperate = false })
    );

    public static IFileProvider ModuleFiles(string folder)
    {
        return new EmbeddedFileProvider(typeof(ServingHost).Assembly, $"Tests.Serving.Fixtures.modules.{folder}");
    }

    public static DashboardSessionSnapshot Session(
        string? name,
        params (string Id, DashboardModuleAccess Access)[] modules
    )
    {
        return new DashboardSessionSnapshot
        {
            Authenticated = true,
            Name = name,
            CanOperate = false,
            Modules = modules.ToDictionary(static m => m.Id, static m => m.Access, StringComparer.Ordinal),
        };
    }

    public static Task<WebApplication> StartAsync(CancellationToken cancellationToken)
    {
        return StartAsync(configure: null, session: null, beforeRouting: null, cancellationToken);
    }

    public static async Task<WebApplication> StartAsync(
        Action<HeadlessDashboardSetupBuilder>? configure,
        DashboardSessionSnapshot? session,
        Action<WebApplication>? beforeRouting,
        CancellationToken cancellationToken
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddHeadlessDashboard(dashboard =>
        {
            dashboard
                .WithNoAuth()
                .AddModule<ServingDiagModule, ServingModuleOptions>("diag")
                .AddModule<ServingLegacyModule, ServingModuleOptions>("legacy");
            configure?.Invoke(dashboard);
        });
        DashboardServing.AddServingServices(builder.Services);
        builder.Services.AddSingleton(
            new DashboardShellAssets(
                new EmbeddedFileProvider(typeof(ServingHost).Assembly, "Tests.Serving.Fixtures.dist")
            )
        );
        builder.Services.AddSingleton<IDashboardSessionSnapshot>(new FixedSessionSnapshot(session ?? Operator));

        var app = builder.Build();
        beforeRouting?.Invoke(app);
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        // Mapped here unless the dashboard's registration already maps the serving routes itself.
        var registration = app.Services.GetRequiredService<DashboardRegistration>();
        Action<RouteGroupBuilder> mapServing = DashboardServing.MapServingEndpoints;

        if (!registration.EndpointMappers.Contains(mapServing))
        {
            registration.EndpointMappers.Add(mapServing);
        }

        app.MapHeadlessDashboard();
        await app.StartAsync(cancellationToken);

        return app;
    }

    /// <summary>The parts of a served document the tests check.</summary>
    public static ParsedDocument Parse(string html)
    {
        var importMap = _ImportMapRegex.Match(html);
        var entry = _EntryRegex.Match(html);
        var states = _StateRegex.Matches(html);

        return new ParsedDocument
        {
            Html = html,
            ImportMapNonce = importMap.Success ? importMap.Groups["nonce"].Value : null,
            ImportMap = importMap.Success
                ? JsonSerializer.Deserialize<JsonElement>(importMap.Groups["json"].Value)
                : null,
            EntrySource = entry.Success ? entry.Groups["src"].Value : null,
            EntryNonce = entry.Success ? entry.Groups["nonce"].Value : null,
            Preloads =
            [
                .. _PreloadRegex.Matches(html).Select(static m => (m.Groups["href"].Value, m.Groups["nonce"].Value)),
            ],
            Stylesheets = [.. _StylesheetRegex.Matches(html).Select(static m => m.Groups["href"].Value)],
            ScriptTags = [.. _ScriptTagRegex.Matches(html).Select(static m => m.Value)],
            StateBlockCount = states.Count,
            State = states.Count == 1 ? JsonSerializer.Deserialize<JsonElement>(states[0].Groups["json"].Value) : null,
        };
    }

    /// <summary>Returns the nonce the Content Security Policy allows scripts by.</summary>
    public static string? PolicyNonce(string policy)
    {
        var match = _PolicyNonceRegex.Match(policy);

        return match.Success ? match.Groups["nonce"].Value : null;
    }

    [GeneratedRegex(
        """<script type="importmap" nonce="(?<nonce>[^"]+)">(?<json>.*?)</script>""",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _ImportMapRegex { get; }

    [GeneratedRegex(
        """<script type="module" src="(?<src>[^"]+)" nonce="(?<nonce>[^"]+)"></script>""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _EntryRegex { get; }

    [GeneratedRegex(
        """<link rel="modulepreload" href="(?<href>[^"]+)" nonce="(?<nonce>[^"]+)">""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _PreloadRegex { get; }

    [GeneratedRegex(
        """<link rel="stylesheet" href="(?<href>[^"]+)">""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _StylesheetRegex { get; }

    [GeneratedRegex(
        """<script type="application/json" id="headless-dashboard-state">(?<json>.*?)</script>""",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _StateRegex { get; }

    [GeneratedRegex("""<script\b[^>]*>""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _ScriptTagRegex { get; }

    [GeneratedRegex("""script-src 'nonce-(?<nonce>[^']+)'""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _PolicyNonceRegex { get; }
}

internal sealed record ParsedDocument
{
    public required string Html { get; init; }

    public required string? ImportMapNonce { get; init; }

    public required JsonElement? ImportMap { get; init; }

    public required string? EntrySource { get; init; }

    public required string? EntryNonce { get; init; }

    public required IReadOnlyList<(string Href, string Nonce)> Preloads { get; init; }

    public required IReadOnlyList<string> Stylesheets { get; init; }

    public required IReadOnlyList<string> ScriptTags { get; init; }

    public required int StateBlockCount { get; init; }

    public required JsonElement? State { get; init; }

    /// <summary>The import map's URLs by specifier.</summary>
    public IReadOnlyDictionary<string, string> Imports =>
        ImportMap!.Value.GetProperty("imports").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
}
