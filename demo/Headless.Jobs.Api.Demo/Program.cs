using Headless.Coordination;
using Headless.Coordination.PostgreSql;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.DependencyInjection;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.EntityFrameworkCore;

// Run a local Postgres first:
// docker run --name postgres -p 5432:5432 -e POSTGRES_PASSWORD=mysecretpassword -d postgres
const string connectionString =
    "User ID=postgres;Password=mysecretpassword;Host=127.0.0.1;Port=5432;Database=headless_jobs_api_demo;";

var builder = WebApplication.CreateBuilder(args);

// The durable operational store now coordinates node membership through Headless.Coordination, so a coordination
// provider must be registered BEFORE AddHeadlessJobs (the durable path fails fast otherwise).
builder.Services.AddHeadlessCoordination(setup => setup.UsePostgreSql(connectionString));

// Jobs setup with a PostgreSQL operational store.
builder.Services.AddHeadlessJobs(options =>
{
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseJobsDbContext<JobsDbContext>(dbOptions =>
            dbOptions.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Headless.Jobs.Api.Demo"))
        );
    });

    options.AddDashboard(dashboard => dashboard.WithNoAuth());
});

var app = builder.Build();

// Apply the Jobs operational store migrations. The coordination provider creates its own tables during host start.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
    await db.Database.MigrateAsync();
}

// Minimal endpoint to schedule the sample job
app.MapPost(
    "/schedule-sample",
    async (ITimeJobManager<TimeJobEntity> manager) =>
    {
        var result = await manager.AddAsync(
            new TimeJobEntity
            {
                Function = "WebApiSample_HelloWorld",
                Description = "Sample API demo job",
                ExecutionTime = DateTime.UtcNow.AddSeconds(5),
            }
        );

        if (!result.IsSucceeded || result.Result is null)
        {
            return Results.Problem(result.Exception?.Message ?? "Failed to schedule sample job.");
        }

        return Results.Ok(new { result.Result.Id, ScheduledFor = result.Result.ExecutionTime });
    }
);
await app.RunAsync();
