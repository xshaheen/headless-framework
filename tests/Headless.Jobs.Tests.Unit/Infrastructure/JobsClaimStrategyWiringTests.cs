// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Customizer;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Infrastructure;

public sealed class JobsClaimStrategyWiringTests
{
    [Fact]
    public void default_configuration_resolves_the_ef_cas_strategy()
    {
        using var provider = _BuildProvider(new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>());

        provider
            .GetRequiredService<IJobsClaimStrategy<TimeJobEntity, CronJobEntity>>()
            .Should()
            .BeOfType<EfCoreCasJobsClaimStrategy<TestJobsDbContext, TimeJobEntity, CronJobEntity>>();
    }

    [Fact]
    public void configured_native_strategy_replaces_the_default()
    {
        var builder = new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>();
        builder.UseClaimStrategy(typeof(FakeJobsClaimStrategy<,,>));

        using var provider = _BuildProvider(builder);

        provider
            .GetRequiredService<IJobsClaimStrategy<TimeJobEntity, CronJobEntity>>()
            .Should()
            .BeOfType<FakeJobsClaimStrategy<TestJobsDbContext, TimeJobEntity, CronJobEntity>>();
    }

    [Fact]
    public void configuring_two_native_strategies_fails_deterministically()
    {
        var builder = new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>();
        builder.UseClaimStrategy(typeof(FakeJobsClaimStrategy<,,>));

        var act = () => builder.UseClaimStrategy(typeof(SecondFakeJobsClaimStrategy<,,>));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already configured*Select exactly one database claim strategy*");
    }

    private static ServiceProvider _BuildProvider(JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity> builder)
    {
        ServiceBuilder.UseJobsDbContext<TestJobsDbContext, TimeJobEntity, CronJobEntity>(
            builder,
            options => options.UseSqlite("Data Source=:memory:")
        );

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IJobsOwnerIdentity>());
        services.AddSingleton(new SchedulerOptionsBuilder());
        builder.ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    private sealed class TestJobsDbContext(DbContextOptions<TestJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options);

    private class FakeJobsClaimStrategy<TDbContext, TTimeJob, TCronJob> : IJobsClaimStrategy<TTimeJob, TCronJob>
        where TDbContext : DbContext
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        public FakeJobsClaimStrategy() { }

        public IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
            TimeJobEntity[] timeJobs,
            CancellationToken cancellationToken
        ) => _Empty<TimeJobEntity>();

        public IAsyncEnumerable<TimeJobEntity> ClaimTimedOutTimeJobsAsync(CancellationToken cancellationToken) =>
            _Empty<TimeJobEntity>();

        public IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimCronJobOccurrencesAsync(
            (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
            CancellationToken cancellationToken
        ) => _Empty<CronJobOccurrenceEntity<TCronJob>>();

        public IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimTimedOutCronJobOccurrencesAsync(
            CancellationToken cancellationToken
        ) => _Empty<CronJobOccurrenceEntity<TCronJob>>();

        private static async IAsyncEnumerable<T> _Empty<T>()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    private sealed class SecondFakeJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>
        : FakeJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>
        where TDbContext : DbContext
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        public SecondFakeJobsClaimStrategy() { }
    }
}
