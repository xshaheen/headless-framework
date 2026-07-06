// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Customizer;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Fluent builder for configuring the Entity Framework Core operational store registered by
/// <c>UseEntityFramework</c>. Allows selecting the DbContext strategy, connection pool size,
/// and database schema.
/// </summary>
/// <typeparam name="TTimeJob">The concrete time job entity type for this application.</typeparam>
/// <typeparam name="TCronJob">The concrete cron job entity type for this application.</typeparam>
public class JobsEfCoreOptionBuilder<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    internal Action<IServiceCollection> ConfigureServices { get; set; } = _ => { };
    internal int PoolSize { get; set; } = 1024;
    internal string Schema { get; set; } = "jobs";

    /// <summary>
    /// Uses the application's existing <typeparamref name="TDbContext"/> to store job data alongside
    /// other application tables. The job entities must be configured in the existing context.
    /// </summary>
    /// <typeparam name="TDbContext">The application <see cref="DbContext"/> that includes the job entities.</typeparam>
    /// <param name="configurationType">Controls how the EF model is registered (pooled or non-pooled).</param>
    public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> UseApplicationDbContext<TDbContext>(
        ConfigurationType configurationType
    )
        where TDbContext : DbContext
    {
        ServiceBuilder.UseApplicationDbContext<TDbContext, TTimeJob, TCronJob>(this, configurationType);
        return this;
    }

    /// <summary>
    /// Uses a dedicated <typeparamref name="TDbContext"/> that derives from
    /// <c>JobsDbContext&lt;TTimeJob, TCronJob&gt;</c> to isolate job storage from the application
    /// database.
    /// </summary>
    /// <typeparam name="TDbContext">A <c>JobsDbContext</c>-derived context for job-only storage.</typeparam>
    /// <param name="optionsAction">Callback to configure the EF Core <see cref="DbContextOptionsBuilder"/>
    /// (e.g., connection string, provider).</param>
    /// <param name="schema">Optional database schema override; defaults to <c>"jobs"</c> when <see langword="null"/>.</param>
    public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> UseJobsDbContext<TDbContext>(
        Action<DbContextOptionsBuilder> optionsAction,
        string? schema = null
    )
        where TDbContext : JobsDbContext<TTimeJob, TCronJob>
    {
        Schema = schema ?? Schema;

        ServiceBuilder.UseJobsDbContext<TDbContext, TTimeJob, TCronJob>(this, optionsAction);
        return this;
    }

    /// <summary>Sets the EF Core DbContext pool size. Must be greater than zero. Default is 1024.</summary>
    /// <param name="poolSize">The maximum number of pooled DbContext instances.</param>
    public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> SetDbContextPoolSize(int poolSize)
    {
        PoolSize = poolSize;
        return this;
    }

    /// <summary>Overrides the database schema used for all job tables. Default is <c>"jobs"</c>.</summary>
    /// <param name="schema">The schema name to use.</param>
    public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> SetSchema(string schema)
    {
        Schema = schema;
        return this;
    }
}
