// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Sequences;
using Headless.UnitOfWork;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A real host serving the unified dashboard at <c>/headless</c> under host authentication with the Sequences module
/// over SQLite on a temporary file. <c>Bearer ops</c> and <c>Bearer viewer</c> may both view.
/// </summary>
internal sealed class SequencesHost : IAsyncDisposable
{
    public const string Api = "/headless/api/sequences";

    /// <summary>The app clock: mid-2026, so the current yearly partition is <c>2026</c>.</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    public static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    /// <summary>The names the default policies register: a yearly gap-free invoice and a monthly fast order.</summary>
    public static void RegisterPolicies(SequencesOptions options)
    {
        options.Policies["invoice"] = new SequencePolicy
        {
            Mode = SequenceMode.GapFree,
            Reset = SequenceReset.Year,
            Format = "INV-{yyyy}-{seq:D6}",
            TimeZone = Cairo,
        };
        options.Policies["order"] = new SequencePolicy { Mode = SequenceMode.Fast, Reset = SequenceReset.Month };
        options.Policies["ticket"] = new SequencePolicy { Format = "T-{seq}" };
        options.Policies["shipment"] = new SequencePolicy { Mode = SequenceMode.Reported, Format = "S-{seq}" };
    }

    private readonly SqliteTestDatabase? _database;

    private SequencesHost(WebApplication app, SqliteTestDatabase? database)
    {
        App = app;
        _database = database;
        Client = app.GetTestClient();
        // The session cookie is Secure, so it is issued and sent only on an https request.
        Client.BaseAddress = new Uri("https://localhost");
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public FakeTimeProvider Time => (FakeTimeProvider)App.Services.GetRequiredService<TimeProvider>();

    /// <summary>
    /// Starts the host. With <paramref name="inspector"/> the module reads that inspector and no sequences are
    /// registered; with neither it and <paramref name="addSequences"/> false, nothing registers an inspector.
    /// </summary>
    public static async Task<SequencesHost> StartAsync(
        CancellationToken cancellationToken,
        bool addSequences = true,
        ISequenceInspector? inspector = null,
        Action<SequencesOptions>? policies = null,
        Action<HeadlessDashboardSetupBuilder>? addModule = null
    )
    {
        var database = addSequences && inspector is null ? SqliteTestDatabase.Create() : null;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication(RoleBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RoleBearerHandler>(RoleBearerHandler.SchemeName, _ => { });
        builder.Services.AddAuthorizationBuilder().AddPolicy("Viewers", policy => policy.RequireRole("ops", "viewer"));

        if (database is not null)
        {
            builder.Services.AddUnitOfWork();
            builder.Services.AddHeadlessSequences(setup =>
            {
                setup.UseSqlite(database.ConnectionString);
                setup.ConfigureOptions(options => (policies ?? RegisterPolicies)(options));
            });
            // A host start only verifies the schema, so the test host creates it first, as a development host does.
            builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        }
        else if (inspector is not null)
        {
            builder.Services.AddSingleton(inspector);
            builder.Services.Configure<SequencesOptions>(options => (policies ?? RegisterPolicies)(options));
        }

        builder.Services.AddHeadlessDashboard(dashboard =>
        {
            dashboard.WithHostAuthentication(host => host.ViewPolicy = "Viewers");

            if (addModule is null)
            {
                dashboard.AddSequences();
            }
            else
            {
                addModule(dashboard);
            }
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        try
        {
            app.MapHeadlessDashboard();
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();

            if (database is not null)
            {
                await database.DisposeAsync();
            }

            throw;
        }

        return new SequencesHost(app, database);
    }

    /// <summary>
    /// Takes <paramref name="count"/> gap-free numbers of <paramref name="name"/> in one committed unit: document
    /// numbers in the partition the policy chooses now, or plain values in <paramref name="partition"/>.
    /// </summary>
    public async Task IssueGapFreeAsync(
        string tenant,
        string name,
        int count,
        CancellationToken cancellationToken,
        string? partition = null
    )
    {
        using var scope = _ChangeTenant(tenant);
        var factory = App.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqliteConnection(_database!.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, cancellationToken);

        for (var i = 0; i < count; i++)
        {
            // A document number takes the partition the policy's reset chooses; an explicit partition is stored as given.
            if (partition is null)
            {
                await unit.Sequences.NextNumberAsync(name, cancellationToken);
            }
            else
            {
                await unit.Sequences.NextAsync(name, partition, cancellationToken);
            }
        }

        await unit.CompleteAsync(cancellationToken);
    }

    /// <summary>Reports <paramref name="value"/> as the reported counter <paramref name="name"/>'s value.</summary>
    public async Task ReportAsync(string tenant, string name, long value, CancellationToken cancellationToken)
    {
        using var scope = _ChangeTenant(tenant);
        var factory = App.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqliteConnection(_database!.ConnectionString);
        await using var unit = await factory.BeginAsync(connection, cancellationToken);
        await unit.Sequences.AdvanceToAsync(name, value, cancellationToken: cancellationToken);
        await unit.CompleteAsync(cancellationToken);
    }

    /// <summary>Takes <paramref name="count"/> fast numbers of <paramref name="name"/>.</summary>
    public async Task IssueFastAsync(
        string tenant,
        string name,
        int count,
        CancellationToken cancellationToken,
        string? partition = null
    )
    {
        using var scope = _ChangeTenant(tenant);
        var generator = App.Services.GetRequiredService<ISequenceGenerator>();

        for (var i = 0; i < count; i++)
        {
            if (partition is null)
            {
                await generator.NextNumberAsync(name, cancellationToken);
            }
            else
            {
                await generator.NextAsync(name, partition, cancellationToken);
            }
        }
    }

    /// <summary>Exchanges <c>Bearer {role}</c> for a dashboard session and returns its cookie header value.</summary>
    public async Task<string> SignInAsync(string role, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/headless/api/auth/session", UriKind.Relative)
        );
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + role);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var cookie = response
            .Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("__Host-", StringComparison.Ordinal));

        return cookie[..cookie.IndexOf(';', StringComparison.Ordinal)];
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? cookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Headless-Dashboard-Channel", "ui");

        return await Client.SendAsync(request, cancellationToken);
    }

    /// <summary>Signs in as a viewer, reads <paramref name="path"/> under the module's API, and returns the answer.</summary>
    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        var cookie = await SignInAsync("viewer", cancellationToken);
        using var response = await SendAsync(HttpMethod.Get, Api + path, cookie, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text.Length == 0 ? "null" : text);

        return (response.StatusCode, document.RootElement.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private IDisposable _ChangeTenant(string tenant) =>
        App.Services.GetRequiredService<ICurrentTenant>().Change(tenant.Length == 0 ? null : tenant);

    /// <summary>Signs in <c>Authorization: Bearer &lt;role&gt;</c> as a user named for, and in, that role.</summary>
    internal sealed class RoleBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestBearer";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();

            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = header["Bearer ".Length..];
            var identity = new ClaimsIdentity([new(ClaimTypes.Name, role), new(ClaimTypes.Role, role)], SchemeName);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), SchemeName)));
        }
    }
}
