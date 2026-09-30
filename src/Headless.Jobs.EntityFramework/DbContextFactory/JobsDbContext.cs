// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.Jobs.DbContextFactory;

public class JobsDbContext<TTimeJob, TCronJob> : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public JobsDbContext(DbContextOptions<JobsDbContext<TTimeJob, TCronJob>> options)
        : base(options) { }

    protected JobsDbContext(DbContextOptions options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var schema = this.GetService<JobsStorageOptions>().Schema;

        var contractCollation = JobsContractCollation.TryResolve(Database.ProviderName);
        var style = JobsStorageNaming.StyleOf(this);

        modelBuilder.ApplyConfiguration(new TimeJobConfigurations<TTimeJob>(schema, style, contractCollation));
        modelBuilder.ApplyConfiguration(new CronJobConfigurations<TCronJob>(schema, style, contractCollation));
        modelBuilder.ApplyConfiguration(
            new CronJobOccurrenceConfigurations<TCronJob>(schema, style, contractCollation)
        );
        base.OnModelCreating(modelBuilder);
        JobsKeyedModelConfiguration.Configure<TTimeJob>(modelBuilder, this);
    }
}

public class JobsDbContext(DbContextOptions<JobsDbContext> options)
    : JobsDbContext<TimeJobEntity, CronJobEntity>(options);
