// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Dashboard;
using Headless.Dashboards.Sandbox;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;

// A local test host for the Headless dashboard, with its Jobs and Messaging modules, started and stopped by the
// project CLI (`make up`, `make down`). It binds to loopback only and exists so a person or
// an agent can drive every dashboard state on demand; it is not a sample of production wiring. See sandboxes/README.md.
var builder = WebApplication.CreateBuilder(args);
var sandbox = builder.Configuration.GetSection("Sandbox").Get<SandboxSettings>() ?? new SandboxSettings();
var postgres =
    sandbox.Store == SandboxStore.Postgres
        ? sandbox.PostgresConnectionString
            ?? throw new InvalidOperationException(
                "Sandbox__PostgresConnectionString is required when Sandbox__Store is Postgres."
            )
        : null;

if (postgres is not null)
{
    // The durable Jobs store coordinates node membership, so a coordination provider must be registered first.
    builder.Services.AddHeadlessCoordination(setup => setup.UsePostgreSql(postgres));
}

// The Headless dashboard always runs in host mode, so the sandbox host sign-in is always registered.
SandboxHostAuthentication.Add(builder.Services, sandbox.ResolveHostSecret());

builder.Services.AddHeadlessDashboard(dashboard =>
{
    dashboard.WithHostAuthentication(host =>
    {
        host.ViewPolicy = SandboxHostAuthentication.ViewPolicy;
        host.OperatePolicy = SandboxHostAuthentication.OperatePolicy;
    });
    // The Jobs module reads the store AddHeadlessJobs configures below, and inherits the host policies above.
    dashboard.AddJobs();
    // The Messaging module reads the stores AddHeadlessMessaging configures below, and inherits the host policies
    // above; its inbox and scheduled operations take the signed-in operator's name for the ledger.
    dashboard.AddMessaging();
});

builder.Services.AddHeadlessJobs(options =>
{
    options.AddModule<Headless.Dashboards.Sandbox.JobsModule>();
    options.ConfigureScheduler(scheduler =>
    {
        // Short cadences so each dashboard state shows within seconds of a scenario call.
        scheduler.ProgressReportInterval = TimeSpan.FromMilliseconds(500);
        scheduler.LeaseDuration = TimeSpan.FromSeconds(30);
        scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(5);
    });

    if (postgres is not null)
    {
        options.UseEntityFramework(ef => ef.UseJobsDbContext<JobsDbContext>(db => db.UseNpgsql(postgres)));
    }
});

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.AddModule<Headless.Dashboards.Sandbox.MessagingModule>();
    // The sandbox consumer's failures are deliberate, so they fail terminally instead of following the default
    // consume policy (immediate, then delayed retries). A terminal Failed generation is what inbox operations such
    // as Force reprocess act on; one still waiting for a retry is refused as Active. RetryPolicy would not do this:
    // it governs publishing, not consuming.
    setup.DefaultFailurePolicy(policy => policy.FailOn<InvalidOperationException>());
    setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
    // Messages stay on the in-memory transport; their storage follows the sandbox store, so with STORE=postgres the
    // published and received rows survive a restart and `make db-q` can read them.
    if (postgres is not null)
    {
        setup.UsePostgreSql(postgres);
    }
    else
    {
        setup.UseInMemoryStorage();
    }

    setup.UseInMemory();
});

// The host start only verifies its tables and queues, so this demo creates them as it starts; a deployed app runs
// `dotnet <app>.dll resources provision` instead.
builder.Services.AddDeployStepsOnStartup();

// Nothing seeds on its own, so this demo seeds as it starts; a deployed app runs `dotnet <app>.dll seed` instead.

var app = builder.Build();

if (postgres is not null)
{
    // The sandbox database is disposable: build the current model directly instead of carrying migrations.
    // `make sandbox-reset` drops it when the model changes.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<JobsDbContext>().Database.EnsureCreatedAsync();
}

app.MapGet("/healthz", () => Results.Text("ok"));

// What the project CLI reads to call the sandbox usable: a read through the Messaging store, which fails until that
// storage answers. It is anonymous because the module's own API sits behind the host sign-in session.
app.MapGet(
    "/sandbox/usable",
    async (IDataStorage storage, CancellationToken cancellationToken) =>
    {
        var statistics = await storage.GetMonitoringApi().GetStatisticsAsync(cancellationToken);
        return Results.Json(
            new
            {
                messaging = "ok",
                statistics.PublishedSucceeded,
                statistics.ReceivedSucceeded,
            }
        );
    }
);

app.MapGet(
    "/",
    () =>
        Results.Json(
            new
            {
                store = sandbox.Store.ToString(),
                headlessDashboard = "/headless",
                jobsModule = "/headless/jobs",
                messagingModule = "/headless/messaging",
                headlessAuth = "host",
                scenarios = SandboxScenarios.Descriptions,
            }
        )
);

app.MapGet("/sandbox/scenarios", () => Results.Json(SandboxScenarios.Descriptions));

app.MapPost(
    "/sandbox/scenarios/{name}",
    async (
        string name,
        IJobScheduler jobs,
        ICronJobManager<CronJobEntity> cronJobs,
        IBus bus,
        CancellationToken cancellationToken
    ) =>
    {
        if (!string.Equals(name, "all", StringComparison.Ordinal) && !SandboxScenarios.Descriptions.ContainsKey(name))
        {
            return Results.NotFound(
                new { error = $"Unknown scenario '{name}'.", known = SandboxScenarios.Descriptions.Keys }
            );
        }

        var created = await SandboxScenarios.RunAsync(name, jobs, cronJobs, bus, cancellationToken);
        return Results.Json(new { scenario = name, created });
    }
);

SandboxHostAuthentication.MapSignInPage(app, "/sandbox/headless-sign-in", "/headless", "Headless dashboard");

app.MapHeadlessDashboard();

await app.RunAsync();
