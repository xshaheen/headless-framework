using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Console.Demo;
using Headless.Jobs.DbContextFactory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Run a local Postgres first:
// docker run --name postgres -p 5432:5432 -e POSTGRES_PASSWORD=mysecretpassword -d postgres
const string connectionString =
    "User ID=postgres;Password=mysecretpassword;Host=127.0.0.1;Port=5432;Database=headless_jobs_console_demo;";

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (_, services) =>
        {
            // The durable operational store coordinates membership through Headless.Coordination, so a coordination
            // provider must be registered before AddHeadlessJobs (the durable path fails fast otherwise).
            services.AddHeadlessCoordination(setup => setup.UsePostgreSql(connectionString));

            // Configure Jobs with a PostgreSQL operational store.
            services.AddHeadlessJobs(options =>
            {
                options.UseEntityFramework(efOptions =>
                {
                    efOptions.UseJobsDbContext<JobsDbContext>(dbOptions =>
                        dbOptions.UseNpgsql(
                            connectionString,
                            npgsql => npgsql.MigrationsAssembly("Headless.Jobs.Console.Demo")
                        )
                    );
                });
            });

            services.AddHostedService<SampleScheduler>();
        }
    )
    .Build();

// Apply the Jobs operational store migrations. The coordination provider creates its own tables during host start.
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
