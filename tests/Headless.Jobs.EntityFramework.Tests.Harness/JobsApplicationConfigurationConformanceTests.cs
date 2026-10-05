// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

public abstract class JobsApplicationConfigurationConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsApplicationConfigurationFixture
{
    public virtual Task application_message_and_scheduled_job_share_transaction(bool commit)
    {
        return _ShareTransactionAsync<ApplicationContext>(
            services => services.AddDbContext<ApplicationContext>(fixture.ConfigureStore),
            commit
        );
    }

    // A HeadlessDbContext takes only its options, so Jobs builds it outside any scope, from its pooled factory and
    // cloned onto the unit's connection for the coordinated write, and the context opens its own scope for the save.
    public virtual Task headless_application_context_shares_transaction(bool pooled, bool commit)
    {
        return _ShareTransactionAsync<HeadlessApplicationContext>(
            services =>
            {
                if (pooled)
                {
                    services.AddHeadlessDbContextPool<HeadlessApplicationContext>(fixture.ConfigureStore);
                }
                else
                {
                    services.AddHeadlessDbContext<HeadlessApplicationContext>(fixture.ConfigureStore);
                }
            },
            commit
        );
    }

    // EF refuses a transaction begun outside a retrying execution strategy, so on a retrying application context
    // host start (the cron seed) and every enlisted write must run inside the context's strategy.
    public virtual Task retrying_application_context_shares_transaction(bool commit)
    {
        return _ShareTransactionAsync<ApplicationContext>(
            services => services.AddDbContext<ApplicationContext>(fixture.ConfigureRetryingStore),
            commit
        );
    }

    // A running node on a retrying application context: the startup seed writes a cron definition, and a scheduled
    // job is claimed, run, and completed by the background scheduler through the same context.
    public virtual async Task retrying_application_context_seeds_and_runs_jobs()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddDbContext<ApplicationContext>(fixture.ConfigureRetryingStore);
        builder.Services.AddHeadlessJobs(jobs =>
        {
            jobs.AddModule<CoordinatedJobsModule>();
            jobs.AddModule<SeededCronJobsModule>();
            fixture.ConfigureApplicationJobs<ApplicationContext>(
                jobs,
                coordination => coordination.ClusterName = "application-retrying"
            );
        });

        using var host = builder.Build();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<ApplicationContext>(host, AbortToken);
        await host.StartAsync(AbortToken);

        try
        {
            (await fixture.CountCronJobsAsync(AbortToken)).Should().Be(1);

            Guid jobId;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                jobId = await scope
                    .ServiceProvider.GetRequiredService<IJobScheduler>()
                    .ScheduleAsync(
                        new CoordinatedFacadeRequest(Guid.NewGuid(), "retrying"),
                        DateTimeOffset.UtcNow,
                        AbortToken
                    );
            }

            (await _WaitForTimeJobStatusAsync(jobId, JobStatus.Succeeded)).Should().Be((int)JobStatus.Succeeded);
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    // A raw-connection unit owns the transaction, so the enlisted Jobs write joins it rather than running under the
    // application context's retrying strategy, which would refuse the caller's transaction.
    public virtual async Task retrying_application_context_enlists_in_connection_unit(bool commit)
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddDbContext<ApplicationContext>(fixture.ConfigureRetryingStore);
        fixture.ConfigureUnitOfWork(builder.Services);
        builder.Services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            jobs.AddModule<CoordinatedJobsModule>();
            fixture.ConfigureApplicationJobs<ApplicationContext>(
                jobs,
                coordination => coordination.ClusterName = "application-retrying-connection"
            );
        });

        using var host = builder.Build();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<ApplicationContext>(host, AbortToken);
        await host.StartAsync(AbortToken);

        try
        {
            var sentinel = new InvalidOperationException("rollback connection unit");
            var operation = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, unit, _, _, ct) =>
                    {
                        await unit.Jobs.ScheduleAsync(
                            new CoordinatedFacadeRequest(Guid.NewGuid(), "connection unit"),
                            new DateTimeOffset(2035, 4, 5, 12, 30, 0, TimeSpan.Zero),
                            ct
                        );
                        if (!commit)
                        {
                            throw sentinel;
                        }
                    },
                    AbortToken
                );

            if (commit)
            {
                await operation();
            }
            else
            {
                (await operation.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            }

            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(commit ? 1 : 0);
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    // AddDbContext and AddHeadlessDbContext register scoped DbContextOptions by default, and a Development host
    // validates scopes, so the singleton Jobs store must build its contexts without resolving scoped options from the
    // root provider.
    public virtual Task scoped_options_application_context_runs_under_scope_validation(bool headless)
    {
        return headless
            ? _RunUnderScopeValidationAsync<HeadlessApplicationContext>(services =>
                services.AddHeadlessDbContext<HeadlessApplicationContext>(fixture.ConfigureStore)
            )
            : _RunUnderScopeValidationAsync<ApplicationContext>(services =>
                services.AddDbContext<ApplicationContext>(fixture.ConfigureStore)
            );
    }

    // Scoped options are resolved once, from a scope Jobs holds for the host's lifetime. A HeadlessDbContext built from
    // them must still open its own scope per context, so its save pipeline's scoped collaborators are not shared by
    // concurrent writes and are disposed with the context.
    public virtual async Task scoped_options_headless_context_opens_a_scope_per_coordinated_write()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        var recorder = new ScopeProbeRecorder();
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(recorder);
        builder.Services.AddHeadlessDbContext<HeadlessApplicationContext>(
            fixture.ConfigureStore,
            options => options.AddSaveEntryProcessor<ScopeProbeProcessor>(ServiceLifetime.Scoped)
        );
        fixture.ConfigureUnitOfWork(builder.Services);
        builder.Services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            jobs.AddModule<CoordinatedJobsModule>();
            fixture.ConfigureApplicationJobs<HeadlessApplicationContext>(
                jobs,
                coordination => coordination.ClusterName = "application-scope-per-write"
            );
        });

        using var host = builder.Build();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<HeadlessApplicationContext>(host, AbortToken);
        await host.StartAsync(AbortToken);

        try
        {
            // Startup writes are not the subject; only the two enlisted writes below are counted.
            recorder.Clear();
            var seenAfterEachWrite = new List<int>();
            for (var write = 0; write < 2; write++)
            {
                await fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, unit, _, _, ct) =>
                    {
                        await unit.Jobs.ScheduleAsync(
                            new CoordinatedFacadeRequest(Guid.NewGuid(), "enlisted"),
                            new DateTimeOffset(2035, 4, 5, 12, 30, 0, TimeSpan.Zero),
                            ct
                        );
                    },
                    AbortToken
                );
                seenAfterEachWrite.Add(recorder.Instances.Count);
            }

            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(2);
            seenAfterEachWrite[0].Should().BePositive("the enlisted write saves through the Headless pipeline");
            seenAfterEachWrite[1]
                .Should()
                .BeGreaterThan(seenAfterEachWrite[0], "the second write's context opens a scope of its own");
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    private async Task _RunUnderScopeValidationAsync<TContext>(Action<IServiceCollection> registerContext)
        where TContext : DbContext
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        registerContext(builder.Services);
        fixture.ConfigureUnitOfWork(builder.Services);
        builder.Services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            jobs.AddModule<CoordinatedJobsModule>();
            fixture.ConfigureApplicationJobs<TContext>(
                jobs,
                coordination => coordination.ClusterName = "application-scope-validation"
            );
        });

        using var host = builder.Build();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<TContext>(host, AbortToken);
        await host.StartAsync(AbortToken);

        try
        {
            await using (var scope = host.Services.CreateAsyncScope())
            {
                await scope
                    .ServiceProvider.GetRequiredService<IJobScheduler>()
                    .ScheduleAsync(
                        new CoordinatedFacadeRequest(Guid.NewGuid(), "autonomous"),
                        new DateTimeOffset(2035, 4, 5, 12, 30, 0, TimeSpan.Zero),
                        AbortToken
                    );
            }

            // The enlisted write builds its context on the unit's connection from the same options.
            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, unit, _, _, ct) =>
                {
                    await unit.Jobs.ScheduleAsync(
                        new CoordinatedFacadeRequest(Guid.NewGuid(), "enlisted"),
                        new DateTimeOffset(2035, 4, 5, 12, 30, 0, TimeSpan.Zero),
                        ct
                    );
                },
                AbortToken
            );

            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(2);
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    private async Task<int> _WaitForTimeJobStatusAsync(Guid jobId, JobStatus expected)
    {
        // The background scheduler completes the job on its own loop, so the test observes the row until it reaches
        // the expected status or the budget runs out and the last status is reported.
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(30);
        var status = -1;
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            (status, _) = await fixture.ReadTimeJobAsync(jobId, AbortToken);
            if (status == (int)expected)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);
        }

        return status;
    }

    private async Task _ShareTransactionAsync<TContext>(Action<IServiceCollection> registerContext, bool commit)
        where TContext : DbContext
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        registerContext(builder.Services);
        builder.Services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            jobs.AddModule<CoordinatedJobsModule>();
            fixture.ConfigureApplicationJobs<TContext>(
                jobs,
                coordination =>
                {
                    coordination.ClusterName = "application-dx";
                }
            );
        });
        builder.Services.AddHeadlessMessaging(messaging =>
        {
            messaging.UseInMemory();
            fixture.ConfigureMessagingStorage(messaging);
        });

        using var host = builder.Build();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<TContext>(host, AbortToken);
        await host.StartAsync(AbortToken);

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<TContext>();
            var request = new CoordinatedFacadeRequest(Guid.NewGuid(), "application transaction");
            var dueAt = new DateTimeOffset(2035, 4, 5, 12, 30, 0, TimeSpan.FromHours(3));

            var sentinel = new InvalidOperationException("rollback application transaction");
            var scheduledId = Guid.Empty;
            var unitOfWorkFactory = services.GetRequiredService<IUnitOfWorkFactory>();
            var operation = async () =>
                await unitOfWorkFactory.RunAsync(
                    context,
                    async (unit, ct) =>
                    {
                        context.Add(new ApplicationProbe { Id = request.Id });
                        await context.SaveChangesAsync(ct);
                        // The enlisted surface: an IBus publish here would write a standalone row that survives
                        // the rollback this test forces.
                        await unit.Outbox.PublishAsync(new ApplicationMessage(request.Id), ct);
                        scheduledId = await unit.Jobs.ScheduleAsync(request, dueAt, ct);
                        if (!commit)
                        {
                            throw sentinel;
                        }
                    },
                    cancellationToken: AbortToken
                );

            if (commit)
            {
                await operation();
            }
            else
            {
                (await operation.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            }

            await using var readScope = host.Services.CreateAsyncScope();
            var readContext = readScope.ServiceProvider.GetRequiredService<TContext>();
            var expectedRows = commit ? 1 : 0;
            (await readContext.Set<ApplicationProbe>().CountAsync(AbortToken)).Should().Be(expectedRows);
            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(expectedRows);
            (await fixture.CountPublishedMessagesAsync(host.Services, AbortToken)).Should().Be(expectedRows);
            if (commit)
            {
                var job = await readContext.Set<TimeJobEntity>().SingleAsync(x => x.Id == scheduledId, AbortToken);
                job.ExecutionTime.Should().BeCloseTo(dueAt.UtcDateTime, TimeSpan.FromMicroseconds(1));
            }
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    private sealed class ApplicationContext(DbContextOptions<ApplicationContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ApplicationProbe>()
                .ToTable("ApplicationProbe", HeadlessStorageDefaults.Schema)
                .HasKey(x => x.Id);
        }
    }

    private sealed class HeadlessApplicationContext(DbContextOptions<HeadlessApplicationContext> options)
        : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<ApplicationProbe>()
                .ToTable("ApplicationProbe", HeadlessStorageDefaults.Schema)
                .HasKey(x => x.Id);
        }
    }

    /// <summary>One code-defined cron function, so the startup seed writes a definition row.</summary>
    private sealed class SeededCronJobsModule : IJobsModule
    {
        private const string _FunctionName = "Application_Retrying_Seeded_Cron";

        // Once a year, so the seeded definition never fires while a test runs.
        private const string _CronExpression = "0 0 0 1 1 *";

        private SeededCronJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            catalog.AddFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [_FunctionName] = new JobFunctionRegistration
                    {
                        CronExpression = _CronExpression,
                        Priority = JobPriority.LongRunning,
                        Delegate = (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 1,
                    },
                }
            );
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [_FunctionName] = new(_FunctionName, null, _CronExpression, JobPriority.LongRunning, 1),
                }
            );
        }
    }

    /// <summary>Collects every distinct scoped processor instance; one instance per DI scope that saved.</summary>
    private sealed class ScopeProbeRecorder
    {
        private readonly ConcurrentDictionary<ScopeProbeProcessor, byte> _instances = new();

        public ICollection<ScopeProbeProcessor> Instances => _instances.Keys;

        public void Record(ScopeProbeProcessor processor) => _instances.TryAdd(processor, 0);

        public void Clear() => _instances.Clear();
    }

    private sealed class ScopeProbeProcessor(ScopeProbeRecorder recorder) : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context) => recorder.Record(this);
    }

    private sealed class ApplicationProbe
    {
        public Guid Id { get; set; }
    }

    private sealed record ApplicationMessage(Guid Id);
}
