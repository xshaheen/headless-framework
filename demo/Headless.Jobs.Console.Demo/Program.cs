using Headless.Jobs;
using Headless.Jobs.Console.Demo;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (_, services) =>
        {
            // Configure Jobs with SQLite operational store (file-based)
            services.AddJobs(options =>
            {
                options.AddOperationalStore(efOptions =>
                {
                    efOptions.UseJobsDbContext<JobsDbContext>(dbOptions =>
                    {
                        dbOptions.UseSqlite(
                            "Data Source=jobs-console.db",
                            b => b.MigrationsAssembly("Headless.Jobs.Console.Demo")
                        );
                    });
                });
            });

            services.AddHostedService<SampleScheduler>();
        }
    )
    .Build();

// Ensure Jobs operational store schema is applied
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
    await db.Database.MigrateAsync();
}

// Build function metadata so JobFunctionProvider.JobFunctions is initialized
JobFunctionProvider.Build();

await host.RunAsync();
