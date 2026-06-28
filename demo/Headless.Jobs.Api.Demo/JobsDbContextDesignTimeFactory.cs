// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Headless.Jobs.Api.Demo;

/// <summary>
/// Design-time only. Lets <c>dotnet ef migrations</c> construct <see cref="JobsDbContext"/> — which resolves its
/// <see cref="JobsEfCoreOptionBuilder{TTimeJob, TCronJob}"/> from DI in <c>OnModelCreating</c> — without booting
/// the app or touching a database. The connection string is parsed only for provider/migrations metadata; no
/// connection is opened by <c>migrations add</c>.
/// </summary>
internal sealed class JobsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    public JobsDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>());
        var serviceProvider = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=5432;Database=headless_jobs_api_demo;Username=postgres;Password=mysecretpassword",
                npgsql => npgsql.MigrationsAssembly("Headless.Jobs.Api.Demo")
            )
            .UseApplicationServiceProvider(serviceProvider)
            .Options;

        return new JobsDbContext(options);
    }
}
