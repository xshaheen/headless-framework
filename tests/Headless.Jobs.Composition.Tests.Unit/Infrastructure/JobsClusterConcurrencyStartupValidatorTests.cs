// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Infrastructure;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Registration;

namespace Tests.Infrastructure;

/// <summary>
/// A job with a cluster-wide limit fails startup, not the first claim, when the host would claim through the portable
/// CAS path that cannot honor the limit.
/// </summary>
[Collection<JobsHelperCollection>]
public sealed class JobsClusterConcurrencyStartupValidatorTests : TestBase
{
    [Fact]
    public async Task should_fail_startup_when_a_cluster_limited_job_has_no_native_claim()
    {
        // when
        var act = () => _ValidateAsync<PlainJobsDbContext>(ef => { });

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                $"*{TestJobs.BillingCloseDay}*no native claim provider*UsePostgreSql(...)*UseSqlServerClaims()*"
            );
    }

    [Fact]
    public async Task should_fail_startup_when_the_model_forces_the_native_claim_back_to_cas()
    {
        // when
        var act = () => _ValidateAsync<FilteredJobsDbContext>(ef => ef.UsePostgreSqlClaims());

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*global query filter*");
    }

    [Fact]
    public async Task should_pass_when_the_native_claim_serves_the_model()
    {
        // when
        var act = () => _ValidateAsync<PlainJobsDbContext>(ef => ef.UsePostgreSqlClaims());

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_pass_when_no_job_has_a_cluster_limit()
    {
        // when
        var act = () => _ValidateAsync<PlainJobsDbContext>(ef => { }, clusterLimit: false);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_pass_on_a_host_that_never_claims()
    {
        // when
        var act = () => _ValidateAsync<PlainJobsDbContext>(ef => { }, runBackgroundServices: false);

        // then
        await act.Should().NotThrowAsync();
    }

    private static async Task _ValidateAsync<TContext>(
        Action<JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>> configureClaims,
        bool clusterLimit = true,
        bool runBackgroundServices = true
    )
        where TContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        await using var provider = _Provider<TContext>(configureClaims, clusterLimit, runBackgroundServices);

        await provider
            .GetServices<IHeadlessStartupValidator>()
            .Single(x =>
                x.GetType().Name.StartsWith("JobsClusterConcurrencyStartupValidator", StringComparison.Ordinal)
            )
            .ValidateAsync(AbortToken);
    }

    private static ServiceProvider _Provider<TContext>(
        Action<JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>> configureClaims,
        bool clusterLimit = true,
        bool runBackgroundServices = true
    )
        where TContext : JobsDbContext<TimeJobEntity, CronJobEntity>
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<INodeMembership>());
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
        {
            if (!runBackgroundServices)
            {
                jobs.DisableBackgroundServices();
            }

            jobs.AddModule<BillingJobsModule>();

            if (clusterLimit)
            {
                jobs.Tune(TestJobs.BillingCloseDay, job => job.ClusterConcurrency(2));
            }

            jobs.UseEntityFramework(ef =>
            {
                ef.UseJobsDbContext<TContext>(db => db.UseSqlite("Data Source=:memory:"));
                configureClaims(ef);
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class PlainJobsDbContext(DbContextOptions<PlainJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options);

    private sealed class FilteredJobsDbContext(DbContextOptions<FilteredJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TimeJobEntity>().HasQueryFilter(x => x.Function != "excluded");
        }
    }
}
