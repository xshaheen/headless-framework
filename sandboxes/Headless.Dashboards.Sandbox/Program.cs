// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Dashboards.Sandbox;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Microsoft.EntityFrameworkCore;

// A local test host for the Jobs and Messaging dashboards, started and stopped by the project CLI (`make up`,
// `make down`). It binds to loopback only and exists so a person or an agent can drive every dashboard state on
// demand; it is not a sample of production wiring. See sandboxes/README.md.
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

    // No auth: live SignalR updates, progress included, are what the sandbox exists to show.
    options.AddDashboard(dashboard => dashboard.WithNoAuth());
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
    // Inbox and scheduled operations refuse an anonymous operator, so Basic auth makes them reachable; without a
    // password the dashboard stays open for read-only checks.
    setup.UseDashboard(dashboard =>
    {
        if (string.IsNullOrEmpty(sandbox.MessagingDashboardPassword))
        {
            dashboard.WithNoAuth();
        }
        else
        {
            dashboard.WithBasicAuth(sandbox.MessagingDashboardUser, sandbox.MessagingDashboardPassword);
        }
    });
});

var app = builder.Build();

if (postgres is not null)
{
    // The sandbox database is disposable: build the current model directly instead of carrying migrations.
    // `make sandbox-reset` drops it when the model changes.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<JobsDbContext>().Database.EnsureCreatedAsync();
}

app.MapGet("/healthz", () => Results.Text("ok"));

app.MapGet(
    "/",
    () =>
        Results.Json(
            new
            {
                store = sandbox.Store.ToString(),
                jobsDashboard = "/jobs/dashboard",
                messagingDashboard = "/messaging",
                messagingAuth = string.IsNullOrEmpty(sandbox.MessagingDashboardPassword) ? "none" : "basic",
                scenarios = SandboxScenarios.Descriptions,
            }
        )
);

app.MapGet("/sandbox/scenarios", () => Results.Json(SandboxScenarios.Descriptions));

app.MapPost(
    "/sandbox/scenarios/{name}",
    async (string name, IJobScheduler jobs, IBus bus, CancellationToken cancellationToken) =>
    {
        if (!string.Equals(name, "all", StringComparison.Ordinal) && !SandboxScenarios.Descriptions.ContainsKey(name))
        {
            return Results.NotFound(
                new { error = $"Unknown scenario '{name}'.", known = SandboxScenarios.Descriptions.Keys }
            );
        }

        var created = await SandboxScenarios.RunAsync(name, jobs, bus, cancellationToken);
        return Results.Json(new { scenario = name, created });
    }
);

await app.RunAsync();
