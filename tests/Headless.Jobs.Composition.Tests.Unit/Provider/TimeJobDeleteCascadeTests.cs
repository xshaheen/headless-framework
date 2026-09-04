// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Threading.Channels;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// Deleting a chain root must take the WHOLE subtree with it. The Parent/Children FK is
/// <c>DeleteBehavior.NoAction</c>, so nothing cascades: the EF provider used to mark only the root rows Deleted,
/// which orphaned every descendant. A surviving non-timed descendant is unreachable forever (every claim path
/// requires <c>ExecutionTime != null</c>), and a surviving timed one whose <c>ParentId</c> was nulled passes the
/// <c>ParentId == null</c> arm of the parent-terminal gate and runs unconditionally at its scheduled time.
/// </summary>
public sealed class TimeJobDeleteCascadeTests : TestBase
{
    private sealed class FakeCronJob : CronJobEntity;

    private static readonly DateTime _Now = new(2026, 08, 02, 12, 00, 00, DateTimeKind.Utc);

    /// <summary>
    /// Builds a linear chain <paramref name="depth"/> nodes long: a timed root plus non-timed <c>OnSuccess</c>
    /// descendants. Four levels, because the old implementation hydrated exactly two (children + grandchildren).
    /// </summary>
    private static TimeJobEntity[] _LinearChain(int depth)
    {
        var nodes = new TimeJobEntity[depth];

        for (var level = 0; level < depth; level++)
        {
            var isRoot = level == 0;
            nodes[level] = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = $"fn-{level}",
                Status = JobStatus.Idle,
                ExecutionTime = isRoot ? _Now.AddMinutes(5) : null,
                RunCondition = isRoot ? null : RunCondition.OnSuccess,
                ParentId = isRoot ? null : nodes[level - 1].Id,
            };
        }

        return nodes;
    }

    [Fact]
    public async Task ef_remove_time_jobs_async_deletes_every_descendant_level()
    {
        await using var fixture = await EfFixture.CreateAsync();
        var sut = fixture.CreateProvider();
        var chain = _LinearChain(depth: 4);
        var survivor = _LinearChain(depth: 2);
        await sut.AddTimeJobsAsync(chain, AbortToken);
        await sut.AddTimeJobsAsync(survivor, AbortToken);

        var deleted = await sut.RemoveTimeJobsAsync([chain[0].Id], AbortToken);

        deleted.Should().Be(chain.Length);
        foreach (var node in chain)
        {
            (await sut.GetTimeJobByIdAsync(node.Id, AbortToken)).Should().BeNull();
        }

        // An unrelated chain is untouched — the walk follows ParentId, it does not clear the table.
        foreach (var node in survivor)
        {
            (await sut.GetTimeJobByIdAsync(node.Id, AbortToken)).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ef_remove_time_jobs_async_deletes_a_subtree_without_touching_its_ancestors()
    {
        await using var fixture = await EfFixture.CreateAsync();
        var sut = fixture.CreateProvider();
        var chain = _LinearChain(depth: 4);
        await sut.AddTimeJobsAsync(chain, AbortToken);

        var deleted = await sut.RemoveTimeJobsAsync([chain[2].Id], AbortToken);

        deleted.Should().Be(2);
        (await sut.GetTimeJobByIdAsync(chain[0].Id, AbortToken)).Should().NotBeNull();
        (await sut.GetTimeJobByIdAsync(chain[1].Id, AbortToken)).Should().NotBeNull();
        (await sut.GetTimeJobByIdAsync(chain[2].Id, AbortToken)).Should().BeNull();
        (await sut.GetTimeJobByIdAsync(chain[3].Id, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task ef_remove_time_jobs_async_retries_the_whole_scope_then_deletes_the_tree_once()
    {
        await using var fixture = await EfFixture.CreateAsync();
        var timeProvider = new FakeTimeProvider();
        var (sut, chain, logger) = await _SeedChainAsync(fixture, timeProvider);
        var survivor = _LinearChain(depth: 2);
        await sut.AddTimeJobsAsync(survivor, AbortToken);
        var attempts = 0;
        sut.OnTreeDeleteBeforeFirstDelete = () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new TransientDbException();
            }

            return Task.CompletedTask;
        };

        try
        {
            var deletion = sut.RemoveTimeJobsAsync([chain[0].Id], AbortToken);
            await _AdvancePastRetriesAsync(logger, timeProvider, expectedRetries: 1);

            (await deletion).Should().Be(chain.Length);
            attempts.Should().Be(2);
            logger.RetryCount.Should().Be(1);
            foreach (var node in chain)
            {
                (await sut.GetTimeJobByIdAsync(node.Id, AbortToken)).Should().BeNull();
            }

            foreach (var node in survivor)
            {
                (await sut.GetTimeJobByIdAsync(node.Id, AbortToken)).Should().NotBeNull();
            }
        }
        finally
        {
            sut.OnTreeDeleteBeforeFirstDelete = null;
        }
    }

    [Fact]
    public async Task ef_remove_time_jobs_async_does_not_retry_cancellation_and_preserves_the_tree()
    {
        await using var fixture = await EfFixture.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var (sut, chain, logger) = await _SeedChainAsync(fixture);
        var attempts = 0;
        sut.OnTreeDeleteBeforeFirstDelete = () =>
        {
            Interlocked.Increment(ref attempts);
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        try
        {
            var action = async () => await sut.RemoveTimeJobsAsync([chain[0].Id], cancellation.Token);

            await action.Should().ThrowAsync<OperationCanceledException>();
            attempts.Should().Be(1);
            logger.RetryCount.Should().Be(0);
            await _AssertRowsRemainAsync(sut, chain);
        }
        finally
        {
            sut.OnTreeDeleteBeforeFirstDelete = null;
        }
    }

    [Fact]
    public async Task ef_remove_time_jobs_async_rethrows_after_retry_exhaustion_and_preserves_the_tree()
    {
        await using var fixture = await EfFixture.CreateAsync();
        var timeProvider = new FakeTimeProvider();
        var (sut, chain, logger) = await _SeedChainAsync(fixture, timeProvider);
        var failure = new TransientDbException();
        var attempts = 0;
        sut.OnTreeDeleteBeforeFirstDelete = () =>
        {
            Interlocked.Increment(ref attempts);
            throw failure;
        };

        try
        {
            var deletion = sut.RemoveTimeJobsAsync([chain[0].Id], AbortToken);
            await _AdvancePastRetriesAsync(logger, timeProvider, expectedRetries: 3);

            var action = async () => await deletion;
            (await action.Should().ThrowAsync<TransientDbException>()).Which.Should().BeSameAs(failure);
            attempts.Should().Be(4);
            logger.RetryCount.Should().Be(3);
            await _AssertRowsRemainAsync(sut, chain);
        }
        finally
        {
            sut.OnTreeDeleteBeforeFirstDelete = null;
        }
    }

    [Fact]
    public async Task ef_remove_time_jobs_async_does_not_retry_non_retryable_failure_and_preserves_the_tree()
    {
        await using var fixture = await EfFixture.CreateAsync();
        var (sut, chain, logger) = await _SeedChainAsync(fixture);
        var failure = new InvalidOperationException("non-retryable");
        var attempts = 0;
        sut.OnTreeDeleteBeforeFirstDelete = () =>
        {
            Interlocked.Increment(ref attempts);
            throw failure;
        };

        try
        {
            var action = async () => await sut.RemoveTimeJobsAsync([chain[0].Id], AbortToken);

            (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
            attempts.Should().Be(1);
            logger.RetryCount.Should().Be(0);
            await _AssertRowsRemainAsync(sut, chain);
        }
        finally
        {
            sut.OnTreeDeleteBeforeFirstDelete = null;
        }
    }

    [Fact]
    public async Task ef_remove_time_jobs_async_with_no_ids_does_not_open_a_context()
    {
        await using var fixture = await EfFixture.CreateAsync();
        var sut = fixture.CreateProvider();

        var deleted = await sut.RemoveTimeJobsAsync([], AbortToken);

        deleted.Should().Be(0);
        fixture.ContextCreationCount.Should().Be(0);
    }

    [Fact]
    public async Task in_memory_remove_time_jobs_async_deletes_every_descendant_level()
    {
        // Provider parity: the in-memory store also stopped at the direct children.
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a" });
        await using var serviceProvider = services.BuildServiceProvider();
        var sut = new JobsInMemoryPersistenceProvider<TimeJobEntity, FakeCronJob>(serviceProvider);
        var chain = _LinearChain(depth: 4);
        var survivor = _LinearChain(depth: 2);
        await sut.AddTimeJobsAsync(chain, AbortToken);
        await sut.AddTimeJobsAsync(survivor, AbortToken);

        var deleted = await sut.RemoveTimeJobsAsync([chain[0].Id], AbortToken);

        deleted.Should().Be(chain.Length);
        foreach (var node in chain)
        {
            (await sut.GetTimeJobByIdAsync(node.Id, AbortToken)).Should().BeNull();
        }

        foreach (var node in survivor)
        {
            (await sut.GetTimeJobByIdAsync(node.Id, AbortToken)).Should().NotBeNull();
        }
    }

    private sealed class EfFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        private readonly DbContextOptions<JobsDbContext> _options;

        private EfFixture(
            SqliteConnection connection,
            ServiceProvider services,
            DbContextOptions<JobsDbContext> options
        )
        {
            _connection = connection;
            _services = services;
            _options = options;
        }

        public static async Task<EfFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(AbortToken);

            var services = new ServiceCollection()
                .AddEntityFrameworkSqlite()
                .AddSingleton(new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>())
                .BuildServiceProvider();

            var options = new DbContextOptionsBuilder<JobsDbContext>()
                .UseSqlite(connection)
                .UseApplicationServiceProvider(services)
                .Options;

            var fixture = new EfFixture(connection, services, options);
            await using var dbContext = new JobsDbContext(options);
            await dbContext.Database.EnsureCreatedAsync(AbortToken);

            return fixture;
        }

        private TestDbContextFactory? _dbContextFactory;

        public int ContextCreationCount => _dbContextFactory?.CreateCount ?? 0;

        public JobsEfCorePersistenceProvider<JobsDbContext, TimeJobEntity, CronJobEntity> CreateProvider(
            TimeProvider? timeProvider = null,
            ILogger? logger = null
        )
        {
            timeProvider ??= TimeProvider.System;
            logger ??= NullLogger.Instance;
            var dbContextFactory = new TestDbContextFactory(_options);
            _dbContextFactory = dbContextFactory;
            var ownerIdentity = new TestOwnerIdentity();
            var schedulerOptions = new SchedulerOptionsBuilder();

            return new(
                dbContextFactory,
                _options,
                timeProvider,
                new SequentialGuidGenerator(SequentialGuidType.Version7),
                ownerIdentity,
                schedulerOptions,
                cache: null,
                new EfCoreCasJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>(
                    dbContextFactory,
                    timeProvider,
                    new SequentialGuidGenerator(SequentialGuidType.Version7),
                    ownerIdentity,
                    schedulerOptions
                ),
                logger
            );
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<JobsDbContext> options)
        : IDbContextFactory<JobsDbContext>
    {
        public int CreateCount { get; private set; }

        public JobsDbContext CreateDbContext()
        {
            CreateCount++;
            return new(options);
        }
    }

    private static async Task _AdvancePastRetriesAsync(
        CapturingLogger logger,
        FakeTimeProvider timeProvider,
        int expectedRetries
    )
    {
        for (var retry = 0; retry < expectedRetries; retry++)
        {
            await logger.Retries.Reader.ReadAsync(AbortToken);
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        await Task.Yield();
    }

    // Seeds one four-level chain on a provider wired with a capturing logger — the scaffolding every retry-pipeline
    // scenario shares, so each test keeps only the seam behavior that distinguishes it.
    private static async Task<(
        JobsEfCorePersistenceProvider<JobsDbContext, TimeJobEntity, CronJobEntity> Sut,
        TimeJobEntity[] Chain,
        CapturingLogger Logger
    )> _SeedChainAsync(EfFixture fixture, TimeProvider? timeProvider = null)
    {
        var logger = new CapturingLogger();
        var sut = fixture.CreateProvider(timeProvider, logger);
        var chain = _LinearChain(depth: 4);
        await sut.AddTimeJobsAsync(chain, AbortToken);
        return (sut, chain, logger);
    }

    private static async Task _AssertRowsRemainAsync(
        JobsEfCorePersistenceProvider<JobsDbContext, TimeJobEntity, CronJobEntity> sut,
        IEnumerable<TimeJobEntity> jobs
    )
    {
        foreach (var job in jobs)
        {
            (await sut.GetTimeJobByIdAsync(job.Id, AbortToken)).Should().NotBeNull();
        }
    }

    private sealed class TransientDbException : DbException
    {
        public override bool IsTransient => true;
    }

    private sealed class CapturingLogger : ILogger
    {
        private int _retryCount;

        public Channel<int> Retries { get; } = Channel.CreateUnbounded<int>();

        public int RetryCount => Volatile.Read(ref _retryCount);

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (eventId.Id == 3002)
            {
                var retryCount = Interlocked.Increment(ref _retryCount);
                Retries.Writer.TryWrite(retryCount);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    private sealed class TestOwnerIdentity : IJobsOwnerIdentity
    {
        public string DisplayOwner => "test-node";

        public CancellationToken MembershipLostToken => CancellationToken.None;

        public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
        {
            owner = DisplayOwner;

            return true;
        }
    }
}
