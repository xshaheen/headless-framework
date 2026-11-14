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

if (sandbox.Auth == SandboxAuth.Host)
{
    SandboxHostAuthentication.Add(builder.Services, sandbox.ResolveHostSecret());
}

builder.Services.AddHeadlessDashboard(dashboard =>
{
    if (sandbox.Auth == SandboxAuth.Host)
    {
        dashboard.WithHostAuthentication(host =>
        {
            host.ViewPolicy = SandboxHostAuthentication.ViewPolicy;
            host.OperatePolicy = SandboxHostAuthentication.OperatePolicy;
        });
    }
    else
    {
        // A fixed credential is safe here only because the sandbox binds to loopback and holds no real data.
        dashboard.WithBasicAuth("admin", "admin");
    }

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
    // Two timeouts open the payments circuit, and it stays open for 30 minutes, long enough to find it and reset it
    // by hand. A consumer's open duration is capped at the host's MaxOpenDuration, so that cap rises with it.
    setup.Options.CircuitBreaker.MaxOpenDuration = TimeSpan.FromHours(1);
    setup.Tune(
        SandboxConsumerIds.Payments,
        consumer =>
            consumer.CircuitBreaker(breaker =>
            {
                breaker.FailureThreshold = 2;
                breaker.OpenDuration = TimeSpan.FromMinutes(30);
            })
    );
    // The stock counts are slow on purpose, and their rows are the backlog a tester reads. A row is stored when its
    // delivery starts, so one delivery at a time would leave the rest of a burst in the transport, where no backlog
    // counts it; the concurrency stores the whole burst at once.
    setup.Tune(SandboxConsumerIds.StockCounts, consumer => consumer.Concurrency(8));
    // One quote every 30 s, the dispatch grace, so a burst waits for its permits past the point where retry pickup
    // claims a row with no lease. The concurrency stores the whole burst at once, as it does for the stock counts.
    setup.Tune(
        SandboxConsumerIds.CarrierQuotes,
        consumer => consumer.Concurrency(8).RateLimit(ConsumerRateLimit.FixedWindow(1, TimeSpan.FromSeconds(30)))
    );
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

// The Contracts view lists declared contracts only. Each keeps the name its type already publishes under by
// convention, so declaring it renames no stored row.
builder.Services.ConfigureMessaging(messaging =>
{
    messaging.Message<SandboxOrderPlaced>(nameof(SandboxOrderPlaced));
    messaging.Message<SandboxPaymentCaptured>(nameof(SandboxPaymentCaptured));
    messaging.Message<SandboxShipmentRequested>(nameof(SandboxShipmentRequested));
    messaging.Message<SandboxStockCounted>(nameof(SandboxStockCounted));
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
    async (string name, [AsParameters] SandboxScenarioServices services, CancellationToken cancellationToken) =>
    {
        if (!string.Equals(name, "all", StringComparison.Ordinal) && !SandboxScenarios.Descriptions.ContainsKey(name))
        {
            return Results.NotFound(
                new { error = $"Unknown scenario '{name}'.", known = SandboxScenarios.Descriptions.Keys }
            );
        }

        var created = await SandboxScenarios.RunAsync(name, services, cancellationToken);
        return Results.Json(new { scenario = name, created });
    }
);

if (sandbox.Auth == SandboxAuth.Host)
{
    SandboxHostAuthentication.MapSignInPage(app, "/sandbox/headless-sign-in", "/headless", "Headless dashboard");
}

app.MapHeadlessDashboard();

await app.RunAsync();
