// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Customizer;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Infrastructure;

public sealed class JobsClaimStrategyWiringTests : TestBase
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
            .BeOfType<CompatibleJobsClaimStrategy<TestJobsDbContext, TimeJobEntity, CronJobEntity>>();
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

    [Fact]
    public async Task filtered_jobs_model_uses_cas_strategy()
    {
        var native = new CountingJobsClaimStrategy<FilteredTimeJob, CronJobEntity>();
        var cas = new CountingJobsClaimStrategy<FilteredTimeJob, CronJobEntity>();
        var strategy = _CreateCompatibleStrategy<FilteredJobsDbContext, FilteredTimeJob>(native, cas);

        await strategy.ClaimTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        native.Calls.Should().Be(0);
        cas.Calls.Should().Be(1);
    }

    [Fact]
    public async Task discriminator_sensitive_jobs_model_uses_cas_strategy()
    {
        var native = new CountingJobsClaimStrategy<DiscriminatedTimeJob, CronJobEntity>();
        var cas = new CountingJobsClaimStrategy<DiscriminatedTimeJob, CronJobEntity>();
        var strategy = _CreateCompatibleStrategy<DiscriminatedJobsDbContext, DiscriminatedTimeJob>(native, cas);

        await strategy.ClaimTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        native.Calls.Should().Be(0);
        cas.Calls.Should().Be(1);
    }

    [Fact]
    public async Task unfiltered_jobs_model_uses_native_strategy()
    {
        var native = new CountingJobsClaimStrategy<TimeJobEntity, CronJobEntity>();
        var cas = new CountingJobsClaimStrategy<TimeJobEntity, CronJobEntity>();
        var strategy = _CreateCompatibleStrategy<PlainJobsDbContext, TimeJobEntity>(native, cas);

        await strategy.ClaimTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        native.Calls.Should().Be(1);
        cas.Calls.Should().Be(0);
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

    private static CompatibleJobsClaimStrategy<TDbContext, TTimeJob, CronJobEntity> _CreateCompatibleStrategy<
        TDbContext,
        TTimeJob
    >(IJobsClaimStrategy<TTimeJob, CronJobEntity> native, IJobsClaimStrategy<TTimeJob, CronJobEntity> cas)
        where TDbContext : DbContext
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        var options = new DbContextOptionsBuilder<TDbContext>().UseSqlite("Data Source=:memory:").Options;
        var factory = new TestDbContextFactory<TDbContext>(options);
        return new(
            factory,
            native,
            cas,
            NullLogger<CompatibleJobsClaimStrategy<TDbContext, TTimeJob, CronJobEntity>>.Instance
        );
    }

    private sealed class TestJobsDbContext(DbContextOptions<TestJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options);

    private sealed class FilteredJobsDbContext(DbContextOptions<FilteredJobsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            _ConfigureJobsModel<FilteredTimeJob>(modelBuilder);
            modelBuilder.Entity<FilteredTimeJob>().HasQueryFilter(x => x.Function != "excluded");
        }
    }

    private sealed class DiscriminatedJobsDbContext(DbContextOptions<DiscriminatedJobsDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            _ConfigureJobsModel<DiscriminatedTimeJob>(modelBuilder);
            modelBuilder
                .Entity<DiscriminatedTimeJob>()
                .HasDiscriminator<string>("JobKind")
                .HasValue<DiscriminatedTimeJob>("standard")
                .HasValue<SpecialTimeJob>("special");
        }
    }

    private sealed class PlainJobsDbContext(DbContextOptions<PlainJobsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            _ConfigureJobsModel<TimeJobEntity>(modelBuilder);
    }

    private static void _ConfigureJobsModel<TTimeJob>(ModelBuilder modelBuilder)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        modelBuilder.Entity<TTimeJob>().HasKey(x => x.Id);
        modelBuilder.Entity<CronJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<CronJobOccurrenceEntity<CronJobEntity>>().HasKey(x => x.Id);
    }

    private sealed class FilteredTimeJob : TimeJobEntity<FilteredTimeJob>;

    private class DiscriminatedTimeJob : TimeJobEntity<DiscriminatedTimeJob>;

    private sealed class SpecialTimeJob : DiscriminatedTimeJob;

    private sealed class TestDbContextFactory<TDbContext>(DbContextOptions<TDbContext> options)
        : IDbContextFactory<TDbContext>
        where TDbContext : DbContext
    {
        public TDbContext CreateDbContext() => (TDbContext)Activator.CreateInstance(typeof(TDbContext), options)!;
    }

    private sealed class CountingJobsClaimStrategy<TTimeJob, TCronJob> : IJobsClaimStrategy<TTimeJob, TCronJob>
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        public int Calls { get; private set; }

        public IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
            TimeJobEntity[] timeJobs,
            CancellationToken cancellationToken
        ) => _Count<TimeJobEntity>();

        public IAsyncEnumerable<TimeJobEntity> ClaimTimedOutTimeJobsAsync(CancellationToken cancellationToken) =>
            _Count<TimeJobEntity>();

        public IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimCronJobOccurrencesAsync(
            (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
            CancellationToken cancellationToken
        ) => _Count<CronJobOccurrenceEntity<TCronJob>>();

        public IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimTimedOutCronJobOccurrencesAsync(
            CancellationToken cancellationToken
        ) => _Count<CronJobOccurrenceEntity<TCronJob>>();

        private async IAsyncEnumerable<T> _Count<T>()
        {
            Calls++;
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

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
