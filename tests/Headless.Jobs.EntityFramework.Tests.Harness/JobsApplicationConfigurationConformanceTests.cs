// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

public abstract class JobsApplicationConfigurationConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsApplicationConfigurationFixture
{
    public virtual async Task application_message_and_scheduled_job_share_transaction(bool commit)
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddDbContext<ApplicationContext>(fixture.ConfigureStore);
        builder.Services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            fixture.ConfigureApplicationJobs<ApplicationContext>(
                jobs,
                coordination =>
                {
                    coordination.ClusterName = "application-dx";
                    coordination.ConfiguredNodeId = "application-dx-node";
                }
            );
            jobs.ConfigureJob<CoordinatedFacadeRequest>(new JobOptions { RequireAtomicEnlistment = true });
        });
        builder.Services.AddHeadlessMessaging(messaging =>
        {
            messaging.UseInMemory();
            fixture.ConfigureMessagingStorage(messaging);
        });

        using var host = builder.Build();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<ApplicationContext>(host, AbortToken);
        await host.Services.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        await host.StartAsync(AbortToken);

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ApplicationContext>();
            var scheduler = services.GetRequiredService<IJobScheduler>();
            var bus = services.GetRequiredService<IBus>();
            var request = new CoordinatedFacadeRequest(Guid.NewGuid(), "application transaction");
            var dueAt = new DateTimeOffset(2035, 4, 5, 12, 30, 0, TimeSpan.FromHours(3));

            var outsideTransaction = () => scheduler.ScheduleAsync(request, dueAt, AbortToken);
            await outsideTransaction.Should().ThrowAsync<InvalidOperationException>();
            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(0);

            var sentinel = new InvalidOperationException("rollback application transaction");
            var scheduledId = Guid.Empty;
            var operation = async () =>
                await context.ExecuteCoordinatedTransactionAsync(
                    async (db, ct) =>
                    {
                        db.Add(new ApplicationProbe { Id = request.Id });
                        await db.SaveChangesAsync(ct);
                        await bus.PublishAsync(new ApplicationMessage(request.Id), ct);
                        scheduledId = await scheduler.ScheduleAsync(request, dueAt, ct);
                        if (!commit)
                        {
                            throw sentinel;
                        }
                    },
                    services,
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
            var readContext = readScope.ServiceProvider.GetRequiredService<ApplicationContext>();
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
            modelBuilder.Entity<ApplicationProbe>().ToTable("ApplicationProbe", "jobs").HasKey(x => x.Id);
        }
    }

    private sealed class ApplicationProbe
    {
        public Guid Id { get; set; }
    }

    private sealed record ApplicationMessage(Guid Id);
}
