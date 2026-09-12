// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public abstract class JobsCronMaterializationConformanceTests(Action<DbContextOptionsBuilder> configureStore) : TestBase
{
    public virtual async Task bulk_reads_definitions_once_after_ordered_locks()
    {
        var log = new CommandLog();
        var factory = await _SetupAsync(log);
        var definitions = _Definitions();
        await _SeedAsync(factory, definitions);
        var candidates = Enumerable
            .Reverse(definitions)
            .SelectMany(definition => new[] { _Occurrence(definition.Id), _Occurrence(definition.Id, 1) })
            .ToArray();
        log.Armed = true;

        (await factory.Store.InsertCronJobOccurrencesAsync(candidates, AbortToken)).Should().Be(6);

        log.Armed = false;
        Logger.LogInformation(
            "Definition phase: {Locks} locking updates + {Reads} definition reads",
            log.LockIds.Count,
            log.Reads
        );
        log.LockIds.Should().Equal(definitions.Select(x => x.Id).Order());
        log.Reads.Should().Be(1);
        log.Phases.Should().Equal("lock", "lock", "lock", "read");
        await using var context = factory.CreateDbContext();
        var stored = await context.Set<CronJobOccurrenceEntity<CronJobEntity>>().ToArrayAsync(AbortToken);
        stored.Should().HaveCount(6);
        foreach (var candidate in candidates.Concat(stored))
        {
            var definition = definitions.Single(x => x.Id == candidate.CronJobId);
            candidate.Function.Should().Be(definition.Function);
            candidate.ContractVersion.Should().Be(definition.ContractVersion);
            candidate.Request.Should().Equal(definition.Request!);
            candidate.CorrelationId.Should().Be(definition.CorrelationId);
            candidate.CausationId.Should().Be(definition.CausationId);
        }
        candidates.Should().OnlyContain(row => row.CronJob == null);
    }

    public virtual async Task empty_batch_issues_no_definition_commands()
    {
        var log = new CommandLog();
        var factory = await _SetupAsync(log);
        log.Armed = true;
        (await factory.Store.InsertCronJobOccurrencesAsync([], AbortToken)).Should().Be(0);
        log.Phases.Should().BeEmpty();
    }

    public virtual async Task missing_definition_rolls_back_and_leaves_inputs_unchanged()
    {
        var log = new CommandLog();
        var factory = await _SetupAsync(log);
        var definition = _Definitions()[0];
        await _SeedAsync(factory, [definition]);
        var candidates = new[]
        {
            _Occurrence(definition.Id),
            _Occurrence(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
        };
        log.Armed = true;
        var insert = () => factory.Store.InsertCronJobOccurrencesAsync(candidates, AbortToken);

        await insert
            .Should()
            .ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("A cron occurrence requires an existing definition.");

        log.Armed = false;
        candidates.Should().OnlyContain(row => row.Function == "stale-caller");
        await using var context = factory.CreateDbContext();
        (await context.Set<CronJobOccurrenceEntity<CronJobEntity>>().CountAsync(AbortToken)).Should().Be(0);
        // A subsequent write to the fenced definition must succeed after the failed transaction rolls back.
        (await factory.Store.InsertCronJobOccurrencesAsync([candidates[0]], AbortToken))
            .Should()
            .Be(1);
    }

    public virtual async Task retry_rebuilds_context_candidates_and_definition_snapshots()
    {
        var log = new CommandLog();
        var fault = new SaveFault();
        var factory = await _SetupAsync(log, fault);
        var definitions = _Definitions();
        await _SeedAsync(factory, definitions);
        var candidates = definitions.Select(definition => _Occurrence(definition.Id)).ToArray();
        fault.Armed = true;
        log.Armed = true;

        (await factory.Store.InsertCronJobOccurrencesAsync(candidates, AbortToken)).Should().Be(3);

        log.Armed = false;
        fault.Armed = false;
        fault.Attempts.Should().Be(2);
        fault.Contexts.Should().HaveCount(2);
        fault.Entities.Should().OnlyHaveUniqueItems();
        log.LockIds.Should().Equal(definitions.Select(x => x.Id).Order().Concat(definitions.Select(x => x.Id).Order()));
        log.Reads.Should().Be(2);
        await using var context = factory.CreateDbContext();
        var stored = await context.Set<CronJobOccurrenceEntity<CronJobEntity>>().ToArrayAsync(AbortToken);
        stored.Should().HaveCount(3);
        foreach (var row in stored.Concat(candidates))
        {
            row.Request.Should().Equal(definitions.Single(x => x.Id == row.CronJobId).Request!);
            row.ExecutionTime.Should().Be(new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }

    public virtual async Task definition_edit_waits_until_the_complete_snapshot_batch_commits()
    {
        var log = new CommandLog { PauseRead = true };
        var factory = await _SetupAsync(log);
        var definitions = _Definitions();
        await _SeedAsync(factory, definitions);
        var candidates = definitions.Select(definition => _Occurrence(definition.Id)).ToArray();
        log.Armed = true;
        var insert = factory.Store.InsertCronJobOccurrencesAsync(candidates, AbortToken);
        await log.ReadReached.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await using var editor = factory.CreateDbContext();
        var edit = editor
            .Set<CronJobEntity>()
            .Where(x => x.Id == definitions[0].Id)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Function, "edited-after-snapshot")
                        .SetProperty(x => x.Request, new byte[] { 99 }),
                AbortToken
            );
        try
        {
            await Task.WhenAny(edit, Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken));
            edit.IsCompleted.Should().BeFalse();
        }
        finally
        {
            log.ResumeRead.TrySetResult();
            await insert;
            await edit;
        }

        log.Armed = false;
        await using var context = factory.CreateDbContext();
        var stored = await context.Set<CronJobOccurrenceEntity<CronJobEntity>>().ToArrayAsync(AbortToken);
        foreach (var occurrence in stored)
        {
            var definition = definitions.Single(x => x.Id == occurrence.CronJobId);
            occurrence.Function.Should().Be(definition.Function);
            occurrence.Request.Should().Equal(definition.Request!);
        }
        (await context.Set<CronJobEntity>().SingleAsync(x => x.Id == definitions[0].Id, AbortToken))
            .Function.Should()
            .Be("edited-after-snapshot");
    }

    private async Task<MaterializationFactory> _SetupAsync(CommandLog log, SaveFault? fault = null)
    {
        var options = new DbContextOptionsBuilder<MaterializationContext>();
        configureStore(options);
        options.ReplaceService<IExecutionStrategyFactory, RetryStrategyFactory>().AddInterceptors(log);
        if (fault is not null)
        {
            options.AddInterceptors(fault);
        }
        var factory = new MaterializationFactory(options.Options);
        await using var context = factory.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE IF EXISTS "jobs_bulk_materialization"."CronJobOccurrences";
            DROP TABLE IF EXISTS "jobs_bulk_materialization"."CronJobs";
            """,
            AbortToken
        );
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);
        return factory;
    }

    private static async Task _SeedAsync(MaterializationFactory factory, CronJobEntity[] definitions)
    {
        await using var context = factory.CreateDbContext();
        context.AddRange(definitions);
        await context.SaveChangesAsync(AbortToken);
    }

    private static CronJobEntity[] _Definitions() =>
        Enumerable
            .Range(1, 3)
            .Select(i => new CronJobEntity
            {
                // The trailing bytes reverse SQL Server's native uniqueidentifier order relative to .NET Guid order.
                Id = Guid.Parse(FormattableString.Invariant($"0000000{i}-0000-0000-0000-00000000000{4 - i}")),
                Function = "materialization-" + i,
                ContractVersion = "1",
                Expression = "0 * * * * *",
                Request = [(byte)i],
                CorrelationId = "correlation-" + i,
                CausationId = "cause-" + i,
            })
            .ToArray();

    private static CronJobOccurrenceEntity<CronJobEntity> _Occurrence(Guid definitionId, int minute = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            Function = "stale-caller",
            Request = [88],
            ExecutionTime = new DateTime(2035, 1, 1, 0, minute, 0, DateTimeKind.Utc),
        };

    private sealed class MaterializationContext(DbContextOptions<MaterializationContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CronJobConfigurations<CronJobEntity>("jobs_bulk_materialization"));
            modelBuilder.ApplyConfiguration(
                new CronJobOccurrenceConfigurations<CronJobEntity>("jobs_bulk_materialization")
            );
        }
    }

    private sealed class MaterializationFactory(DbContextOptions<MaterializationContext> options)
        : IDbContextFactory<MaterializationContext>
    {
        public MaterializationContext CreateDbContext() => new(options);

        public JobsEfCorePersistenceProvider<MaterializationContext, TimeJobEntity, CronJobEntity> Store
        {
            get
            {
                var guidGenerator = new SequentialGuidGenerator(SequentialGuidType.Version7);
                var owner = new OwnerIdentity();
                var scheduler = new SchedulerOptionsBuilder();
                return new(
                    this,
                    options,
                    TimeProvider.System,
                    guidGenerator,
                    owner,
                    scheduler,
                    null,
                    new EfCoreCasJobsClaimStrategy<MaterializationContext, TimeJobEntity, CronJobEntity>(
                        this,
                        TimeProvider.System,
                        guidGenerator,
                        owner,
                        scheduler
                    ),
                    NullLogger.Instance
                );
            }
        }
    }

    private sealed class OwnerIdentity : IJobsOwnerIdentity
    {
        public string DisplayOwner => "materialization-tests";
        public CancellationToken MembershipLostToken => AbortToken;

        public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
        {
            owner = DisplayOwner;
            return true;
        }
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool PauseRead { get; set; }
        public int Reads { get; private set; }
        public List<Guid> LockIds { get; } = [];
        public List<string> Phases { get; } = [];
        public TaskCompletionSource ReadReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Armed
                && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("ScheduleRevision", StringComparison.Ordinal)
            )
            {
                LockIds.Add(command.Parameters.Cast<DbParameter>().Select(x => x.Value).OfType<Guid>().Single());
                Phases.Add("lock");
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Armed
                && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("CronJobs", StringComparison.Ordinal)
            )
            {
                Reads++;
                Phases.Add("read");
                if (PauseRead)
                {
                    PauseRead = false;
                    ReadReached.TrySetResult();
                    await ResumeRead.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }
            return result;
        }
    }

    public sealed class RetryFaultException : Exception;

    private sealed class RetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is RetryFaultException;
    }

    private sealed class RetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryStrategy(dependencies);
    }

    private sealed class SaveFault : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Attempts { get; private set; }
        public HashSet<DbContextId> Contexts { get; } = [];
        public List<object> Entities { get; } = [];

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                var context = eventData.Context!;
                Contexts.Add(context.ContextId);
                var rows = context
                    .ChangeTracker.Entries<CronJobOccurrenceEntity<CronJobEntity>>()
                    .Select(x => x.Entity)
                    .ToArray();
                Entities.AddRange(rows);
                if (++Attempts == 1)
                {
                    foreach (var row in rows)
                    {
                        row.Request![0] = 99;
                        row.ExecutionTime = row.ExecutionTime.AddHours(1);
                    }
                    throw new RetryFaultException();
                }
            }
            return ValueTask.FromResult(result);
        }
    }
}
