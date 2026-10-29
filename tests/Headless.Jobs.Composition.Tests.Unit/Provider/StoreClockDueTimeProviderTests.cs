// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Jobs;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// A run-now time job is stamped by the store inside its write, from the store's own clock. The in-memory store's clock
/// is its <see cref="TimeProvider"/>; the generic EF store on a backend with no statement clock (SQLite) falls back to
/// the host's <see cref="TimeProvider"/>, which on an embedded database is the store clock.
/// </summary>
public sealed class StoreClockDueTimeProviderTests : TestBase
{
    private const string _Function = "store-clock-fn";

    private static readonly DateTimeOffset _StoreNow = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_stamp_a_run_now_root_from_the_in_memory_store_clock_when_seeded()
    {
        var clock = new FakeTimeProvider(_StoreNow);
        var store = _InMemoryStore(clock);
        var job = _Job(executionTime: null);

        var result = await store.AddTimeJobsAsync([job], _RunNow(job.Id), AbortToken);

        result.StoreUtcNow.Should().Be(_StoreNow.UtcDateTime);
        result.AffectedRows.Should().Be(1);
        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.ExecutionTime.Should().Be(_StoreNow.UtcDateTime);
    }

    [Fact]
    public async Task should_stamp_every_run_now_root_of_a_batch_with_one_instant_and_keep_an_absolute_root_when_seeded()
    {
        var clock = new FakeTimeProvider(_StoreNow);
        var store = _InMemoryStore(clock);
        var absoluteTime = _StoreNow.UtcDateTime.AddHours(3);
        var runNow = new[] { _Job(null), _Job(null), _Job(null) };
        var absolute = _Job(absoluteTime);

        var result = await store.AddTimeJobsAsync(
            [.. runNow, absolute],
            _RunNow([.. runNow.Select(x => x.Id)]),
            AbortToken
        );

        result.AffectedRows.Should().Be(4);
        foreach (var job in runNow)
        {
            (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.ExecutionTime.Should().Be(_StoreNow.UtcDateTime);
        }

        (await store.GetTimeJobByIdAsync(absolute.Id, AbortToken))!.ExecutionTime.Should().Be(absoluteTime);
    }

    [Fact]
    public async Task should_stamp_an_idempotent_run_now_creation_and_write_nothing_on_a_hit_within_the_window()
    {
        var clock = new FakeTimeProvider(_StoreNow);
        var store = _InMemoryStore(clock);
        var winner = _Job(null);

        var created = await store.AddIdempotentTimeJobAsync(
            winner,
            "key-1",
            TimeSpan.FromMinutes(10),
            _RunNow(winner.Id),
            AbortToken
        );
        clock.Advance(TimeSpan.FromMinutes(1));
        var loser = _Job(null);
        var hit = await store.AddIdempotentTimeJobAsync(
            loser,
            "key-1",
            TimeSpan.FromMinutes(10),
            _RunNow(loser.Id),
            AbortToken
        );

        created.Created.Should().BeTrue();
        created.StoreUtcNow.Should().Be(_StoreNow.UtcDateTime);
        (await store.GetTimeJobByIdAsync(winner.Id, AbortToken))!.ExecutionTime.Should().Be(_StoreNow.UtcDateTime);
        hit.Created.Should().BeFalse();
        hit.JobId.Should().Be(winner.Id);
        (await store.GetTimeJobByIdAsync(loser.Id, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_stamp_a_run_now_root_from_the_host_clock_when_the_ef_backend_has_no_statement_clock()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(AbortToken);
        await using var services = new ServiceCollection()
            .AddEntityFrameworkSqlite()
            .AddSingleton(new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>())
            .AddSingleton(new JobsStorageOptions())
            .BuildServiceProvider();
        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseSqlite(connection)
            .UseApplicationServiceProvider(services)
            .Options;
        await using (var schema = new JobsDbContext(options))
        {
            await schema.Database.EnsureCreatedAsync(AbortToken);
        }

        var hostClock = new FakeTimeProvider(_StoreNow);
        var store = _SqliteStore(options, hostClock);
        var job = _Job(null);

        var result = await store.AddTimeJobsAsync([job], _RunNow(job.Id), AbortToken);

        result.StoreUtcNow.Should().Be(_StoreNow.UtcDateTime);
        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.ExecutionTime.Should().Be(_StoreNow.UtcDateTime);
    }

    private static TimeJobDueTimeSeeder _RunNow(params Guid[] relativeRootIds)
    {
        return (root, storeUtcNow) => relativeRootIds.Contains(root.Id) ? storeUtcNow : null;
    }

    private static TimeJobEntity _Job(DateTime? executionTime)
    {
        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            Request = [],
            ExecutionTime = executionTime,
        };
    }

    private static JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> _InMemoryStore(TimeProvider clock)
    {
        var services = new ServiceCollection()
            .AddSingleton(clock)
            .AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7))
            .BuildServiceProvider();

        return new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
    }

    private static JobsEfCorePersistenceProvider<JobsDbContext, TimeJobEntity, CronJobEntity> _SqliteStore(
        DbContextOptions<JobsDbContext> options,
        TimeProvider hostClock
    )
    {
        var contexts = new OptionsDbContextFactory(options);
        var ownerIdentity = new FixedOwnerIdentity();
        var schedulerOptions = new SchedulerOptionsBuilder();
        var guids = new SequentialGuidGenerator(SequentialGuidType.Version7);

        return new(
            contexts,
            options,
            hostClock,
            guids,
            ownerIdentity,
            schedulerOptions,
            cache: null,
            new EfCoreCasJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>(
                contexts,
                hostClock,
                guids,
                ownerIdentity,
                schedulerOptions
            ),
            NullLogger.Instance
        );
    }

    private sealed class OptionsDbContextFactory(DbContextOptions<JobsDbContext> options)
        : IDbContextFactory<JobsDbContext>
    {
        public JobsDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedOwnerIdentity : IJobsOwnerIdentity
    {
        public string DisplayOwner => "store-clock-node";

        public CancellationToken MembershipLostToken => CancellationToken.None;

        public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
        {
            owner = DisplayOwner;
            return true;
        }
    }
}
