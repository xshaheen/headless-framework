using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.DependencyInjection;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Jobs setup with SQLite operational store (file-based)
builder.Services.AddJobs(options =>
{
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseJobsDbContext<JobsDbContext>(dbOptions =>
        {
            dbOptions.UseSqlite("Data Source=jobs-webapi.db", b => b.MigrationsAssembly("Headless.Jobs.Api.Demo"));
        });
    });

    options.AddDashboard(dashboard =>
    {
        dashboard.WithNoAuth();
    });
});

var app = builder.Build();

// Ensure Jobs operational store schema is applied
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
    await db.Database.MigrateAsync();
}

// Activate Jobs job processor (mirrors docs' minimal setup)
AspNetCoreExtensions.UseJobs(app);

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
