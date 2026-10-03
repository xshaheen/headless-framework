// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting.Initialization;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
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

    private sealed class ApplicationProbe
    {
        public Guid Id { get; set; }
    }

    private sealed record ApplicationMessage(Guid Id);
}
