// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Configurations;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public abstract class JobsKeyedSchedulingConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task keyed_provider_operation_matrix_survives_restart()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        using (var host = fixture.BuildHost("keyed-matrix"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
            var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            await JobsKeyedSchedulingScenarios.RunAsync(store, AbortToken);
            var claim = new EfCoreCasJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>(
                host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>(),
                host.Services.GetRequiredService<TimeProvider>(),
                host.Services.GetRequiredService<IGuidGenerator>(),
                new FixedOwnerIdentity(),
                host.Services.GetRequiredService<SchedulerOptionsBuilder>()
            );
            await JobsKeyedSchedulingScenarios.RunClaimRacesAsync(
                store,
                async candidate =>
                    (await claim.ClaimTimeJobsAsync([candidate], AbortToken).ToArrayAsync(AbortToken)).Length == 1,
                AbortToken
            );
            await JobsKeyedSchedulingScenarios.RunLegacyMutationRacesAsync(store, AbortToken);

            var unknownKey = new JobKey("unknown-algorithm");
            var unknown = await store.ScheduleKeyedTimeJobAsync(
                unknownKey,
                JobsKeyedSchedulingScenarios.Candidate(),
                cancellationToken: AbortToken
            );
            await using var context = await host
                .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
                .CreateDbContextAsync(AbortToken);
            await context
                .Set<TimeJobEntity>()
                .Where(row => row.Id == unknown.RunId)
                .ExecuteUpdateAsync(setter => setter.SetProperty(row => row.FingerprintAlgorithm, "v99"), AbortToken);
            var observe = async () =>
                await store.ScheduleKeyedTimeJobAsync(
                    unknownKey,
                    JobsKeyedSchedulingScenarios.Candidate(),
                    cancellationToken: AbortToken
                );
            await observe.Should().ThrowAsync<NotSupportedException>();
            (await store.ScheduleKeyedTimeJobAsync(unknownKey, JobsKeyedSchedulingScenarios.Candidate(), 1, AbortToken))
                .Disposition.Should()
                .Be(JobScheduleDisposition.Replaced);
        }

        using var restarted = fixture.BuildHost("keyed-restarted");
        var reopened = restarted.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var result = await reopened.ScheduleKeyedTimeJobAsync(
            new JobKey("invoice-42"),
            JobsKeyedSchedulingScenarios.Candidate([4]),
            cancellationToken: AbortToken
        );
        result.Disposition.Should().Be(JobScheduleDisposition.Existing);
        result.Generation.Should().Be(2);
        result.State.Should().Be(Headless.Jobs.Enums.JobStatus.Cancelled);
    }

    public virtual async Task keyed_constraints_follow_custom_column_mappings()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        using var host = fixture.BuildMappedHost<RenamedKeyedJobsDbContext>("keyed-mapped", "jobs");
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<RenamedKeyedJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RenamedKeyedJobsDbContext>(host, AbortToken);

        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        await JobsKeyedSchedulingScenarios.RunAsync(store, AbortToken);
        var retained = await context
            .Set<TimeJobEntity>()
            .AsNoTracking()
            .FirstAsync(row => row.BusinessKey != null, AbortToken);
        var clearFingerprint = async () =>
            await context
                .Set<TimeJobEntity>()
                .Where(row => row.Id == retained.Id)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(row => row.IntentFingerprint, (string?)null),
                    AbortToken
                );
        await clearFingerprint.Should().ThrowAsync<DbException>();
    }

    public virtual async Task public_job_configurations_create_valid_keyed_schema_without_customizer()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        var options = new DbContextOptionsBuilder<DirectJobsDbContext>();
        fixture.ConfigureStore(options);
        await using var context = new DirectJobsDbContext(options.Options);
        var creator = (RelationalDatabaseCreator)context.GetService<IDatabaseCreator>();
        await creator.CreateTablesAsync(AbortToken);
    }

    private sealed class DirectJobsDbContext(DbContextOptions<DirectJobsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new TimeJobConfigurations<TimeJobEntity>("jobs"));
            modelBuilder.ApplyConfiguration(new CronJobConfigurations<CronJobEntity>("jobs"));
            modelBuilder.ApplyConfiguration(new CronJobOccurrenceConfigurations<CronJobEntity>("jobs"));
        }
    }

    private sealed class RenamedKeyedJobsDbContext(DbContextOptions<RenamedKeyedJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var row = modelBuilder.Entity<TimeJobEntity>();
            row.Property(job => job.BusinessKey).HasColumnName("business_key");
            row.Property(job => job.IntentFingerprint).HasColumnName("intent_hash");
            row.Property(job => job.FingerprintAlgorithm).HasColumnName("hash_algorithm");
            row.Property(job => job.Generation).HasColumnName("key_generation");
            row.Property(job => job.IsCurrentGeneration).HasColumnName("is_current");
            row.Property(job => job.TenantId).HasColumnName("tenant_key");
            row.Property(job => job.Function).HasColumnName("function_key");
            row.Property(job => job.ParentId).HasColumnName("parent_key");
            row.Property(job => job.RunCondition).HasColumnName("run_condition");
        }
    }

    private sealed class FixedOwnerIdentity : IJobsOwnerIdentity
    {
        public string DisplayOwner => "keyed-tests@1";

        public bool TryGetStampOwner([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? stampOwner)
        {
            stampOwner = DisplayOwner;
            return true;
        }

        public CancellationToken MembershipLostToken => CancellationToken.None;
    }
}
