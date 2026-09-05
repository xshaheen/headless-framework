// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Configurations;
using Headless.Jobs.Customizer;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public abstract class JobsKeyedSchedulingConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    protected TFixture Fixture => fixture;

    public virtual async Task keyed_provider_operation_matrix_survives_restart()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        using (var host = fixture.BuildHost("keyed-matrix"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
            var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            await JobsKeyedSchedulingScenarios.RunAsync(store, AbortToken);
            await using var parentMapping = await host
                .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
                .CreateDbContextAsync(AbortToken);
            await JobsKeyedSchedulingScenarios.RunParentAttachmentRejectionsAsync(
                store,
                host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>(),
                (job, parentId) => parentMapping.Entry(job).Property(row => row.ParentId).CurrentValue = parentId,
                AbortToken
            );
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

    public virtual async Task fresh_schema_enforces_keyed_metadata_and_scoped_uniqueness()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        using var host = fixture.BuildHost("keyed-schema");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var factory = host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>();
        var parent = JobsKeyedSchedulingScenarios.Candidate();
        await store.AddTimeJobsAsync([parent], AbortToken);

        foreach (var tenant in new string?[] { null, "tenant-a" })
        {
            var candidate = JobsKeyedSchedulingScenarios.Candidate();
            candidate.TenantId = tenant;
            var key = new JobKey("schema-deadline");
            var first = await store.ScheduleKeyedTimeJobAsync(key, candidate, cancellationToken: AbortToken);
            var replacement = JobsKeyedSchedulingScenarios.Candidate([4]);
            replacement.TenantId = tenant;
            var current = await store.ScheduleKeyedTimeJobAsync(key, replacement, 1, AbortToken);
            first.Disposition.Should().Be(JobScheduleDisposition.Created);
            current.Disposition.Should().Be(JobScheduleDisposition.Replaced);
            var currentId = current.RunId!.Value;

            // Direct EF writes bypass the Jobs API, proving the actual database constraints independently.
            foreach (
                var property in new[]
                {
                    nameof(TimeJobEntity.BusinessKey),
                    nameof(TimeJobEntity.IntentFingerprint),
                    nameof(TimeJobEntity.FingerprintAlgorithm),
                    nameof(TimeJobEntity.Generation),
                    nameof(TimeJobEntity.IsCurrentGeneration),
                }
            )
            {
                await assertRejectedUpdate(currentId, property, null);
            }

            await assertRejectedUpdate(currentId, nameof(TimeJobEntity.Generation), 0L);
            await assertRejectedUpdate(currentId, nameof(TimeJobEntity.Generation), -1L);
            await assertRejectedUpdate(currentId, nameof(TimeJobEntity.ParentId), parent.Id);
            await assertRejectedUpdate(
                currentId,
                nameof(TimeJobEntity.RunCondition),
                Headless.Jobs.Enums.RunCondition.OnSuccess
            );

            // A different generation isolates the current-key index from the generation-history index.
            await assertRejectedDuplicate(currentId, 3L);
            await assertRejectedDuplicate(first.RunId!.Value, 1L);
            await using var observed = await factory.CreateDbContextAsync(AbortToken);
            var rows = await observed
                .Set<TimeJobEntity>()
                .Where(row => row.BusinessKey == key.Value && row.TenantId == tenant)
                .OrderBy(row => row.Generation)
                .ToListAsync(AbortToken);
            rows.Select(row => row.Generation).Should().Equal(1L, 2L);
            rows.Count(row => row.IsCurrentGeneration == true).Should().Be(1);
        }

        async Task assertRejectedUpdate(Guid id, string property, object? value)
        {
            await using var context = await factory.CreateDbContextAsync(AbortToken);
            var row = await context.Set<TimeJobEntity>().SingleAsync(row => row.Id == id, AbortToken);
            context.Entry(row).Property(property).CurrentValue = value;
            var write = () => context.SaveChangesAsync(AbortToken);
            var failure = await write.Should().ThrowAsync<DbUpdateException>();
            failure.Which.InnerException.Should().BeAssignableTo<DbException>();
        }

        async Task assertRejectedDuplicate(Guid id, long generation)
        {
            await using var context = await factory.CreateDbContextAsync(AbortToken);
            var row = await context.Set<TimeJobEntity>().AsNoTracking().SingleAsync(row => row.Id == id, AbortToken);
            row.Id = Guid.NewGuid();
            context.Add(row);
            context.Entry(row).Property(nameof(TimeJobEntity.Generation)).CurrentValue = generation;
            var write = () => context.SaveChangesAsync(AbortToken);
            var failure = await write.Should().ThrowAsync<DbUpdateException>();
            failure.Which.InnerException.Should().BeAssignableTo<DbException>();
        }
    }

    public virtual async Task manual_job_configuration_requires_explicit_ordinal_scope()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        using var host = _BuildManualHost<DirectJobsDbContext>();
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<DirectJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        var creator = (RelationalDatabaseCreator)context.GetService<IDatabaseCreator>();
        await creator.CreateTablesAsync(AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        await store.AddTimeJobsAsync([JobsKeyedSchedulingScenarios.Candidate()], AbortToken);
        var schedule = async () =>
            await store.ScheduleKeyedTimeJobAsync(
                new JobKey("manual"),
                JobsKeyedSchedulingScenarios.Candidate(),
                cancellationToken: AbortToken
            );
        await schedule.Should().ThrowAsync<InvalidOperationException>().WithMessage("*collation*");
        var cancel = async () =>
            await store.CancelKeyedTimeJobAsync(new JobKeyScope("deadline"), new JobKey("manual"), 1, AbortToken);
        await cancel.Should().ThrowAsync<InvalidOperationException>().WithMessage("*collation*");
        (await context.Set<TimeJobEntity>().CountAsync(AbortToken)).Should().Be(1);
    }

    public virtual async Task coordinated_manual_nonordinal_model_rejects_keyed_operations_before_middleware()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        var probe = new JobsScheduleMiddlewareProbe();
        using var host = _BuildManualHost<DirectJobsDbContext>(probe);
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<DirectJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        var creator = (RelationalDatabaseCreator)context.GetService<IDatabaseCreator>();
        await creator.CreateTablesAsync(AbortToken);
        await fixture.CreateProbeTableAsync(AbortToken);
        var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var ordinary = JobsKeyedSchedulingScenarios.Candidate();
        ordinary.Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName;
        await manager.AddAsync(ordinary, AbortToken);
        probe.Calls.Should().Be(1);

        await fixture.RunCoordinatedTransactionAsync(
            host.Services,
            async (connection, transaction, ct) =>
            {
                var keyed = JobsKeyedSchedulingScenarios.Candidate();
                keyed.Function = ordinary.Function;
                keyed.RequireAtomicEnlistment = true;
                var key = new JobKey("nonordinal-preflight");
                var schedule = () => manager.ScheduleKeyedAsync(key, keyed, cancellationToken: ct);
                await schedule.Should().ThrowAsync<InvalidOperationException>().WithMessage("*collation*");
                probe
                    .Calls.Should()
                    .Be(
                        1,
                        "the ordinary call proves middleware is live, but invalid keyed models must reject before it"
                    );
                await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);

                var cancel = () =>
                    manager.CancelKeyedAsync(
                        new JobKeyScope(ordinary.Function),
                        key,
                        1,
                        requireAtomicEnlistment: true,
                        cancellationToken: ct
                    );
                await cancel.Should().ThrowAsync<InvalidOperationException>().WithMessage("*collation*");
                probe.Calls.Should().Be(1);
                connection.State.Should().Be(System.Data.ConnectionState.Open);
                transaction.Connection.Should().BeSameAs(connection);
                await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
            },
            AbortToken
        );

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(2);
        (await context.Set<TimeJobEntity>().CountAsync(AbortToken)).Should().Be(1);
        (await context.Set<TimeJobEntity>().AnyAsync(row => row.BusinessKey != null, AbortToken)).Should().BeFalse();
    }

    public virtual async Task manual_ordinal_job_configuration_preserves_key_scopes()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        using var host = _BuildManualHost<OrdinalJobsDbContext>();
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<OrdinalJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        var creator = (RelationalDatabaseCreator)context.GetService<IDatabaseCreator>();
        await creator.CreateTablesAsync(AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        await JobsKeyedSchedulingScenarios.RunAsync(store, AbortToken);
    }

    public virtual async Task coordinated_add_rejects_retained_keyed_parent_before_batch_effects()
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        using var host = fixture.BuildCoordinatedEnqueueHost("keyed-parent-guard");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var key = new JobKey("coordinated-parent");
        var first = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate(),
            cancellationToken: AbortToken
        );
        var second = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate([4]),
            1,
            AbortToken
        );
        await using var parentMapping = await host
            .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        foreach (var parentId in new[] { first.RunId!.Value, second.RunId!.Value })
        {
            var child = JobsKeyedSchedulingScenarios.Candidate();
            parentMapping.Entry(child).Property(row => row.ParentId).CurrentValue = parentId;
            var unrelated = JobsKeyedSchedulingScenarios.Candidate();
            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, _, ct) =>
                {
                    var coordinator = host.Services.GetRequiredService<ICurrentCommitCoordinator>().Current!;
                    coordinator.TryGetCapability<IRelationalCommitContext>(out var relational).Should().BeTrue();
                    var write = async () =>
                        await ((ICoordinatedJobWriter<TimeJobEntity, CronJobEntity>)store).WriteTimeJobsAsync(
                            [unrelated, child],
                            relational!,
                            ct
                        );
                    await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*keyed*parent*");
                },
                AbortToken
            );
            (await store.GetTimeJobByIdAsync(child.Id, AbortToken)).Should().BeNull();
            (await store.GetTimeJobByIdAsync(unrelated.Id, AbortToken)).Should().BeNull();
        }
    }

    private IHost _BuildManualHost<TContext>(JobsScheduleMiddlewareProbe? scheduleProbe = null)
        where TContext : DbContext
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessCoordination(fixture.ConfigureCoordination);
        builder.Services.AddDbContext<TContext>(fixture.ConfigureStore);
        builder.Services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.UseEntityFramework(ef =>
                ef.UseApplicationDbContext<TContext>(ConfigurationType.IgnoreModelCustomizer)
            );
        });
        if (scheduleProbe is not null)
        {
            builder.Services.AddSingleton(scheduleProbe);
            fixture.ConfigureCommitCoordination(builder.Services);
        }
        return builder.Build();
    }

    private sealed class OrdinalJobsDbContext(DbContextOptions<OrdinalJobsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var collation = string.Equals(
                Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal
            )
                ? "Latin1_General_100_BIN2"
                : "C";
            modelBuilder.ApplyConfiguration(new TimeJobConfigurations<TimeJobEntity>("jobs", collation));
            modelBuilder.ApplyConfiguration(new CronJobConfigurations<CronJobEntity>("jobs", collation));
            modelBuilder.ApplyConfiguration(new CronJobOccurrenceConfigurations<CronJobEntity>("jobs", collation));
        }
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
