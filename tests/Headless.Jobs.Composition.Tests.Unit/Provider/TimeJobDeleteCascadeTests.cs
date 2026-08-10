// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
using Microsoft.Extensions.Logging.Abstractions;

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

        public JobsEfCorePersistenceProvider<JobsDbContext, TimeJobEntity, CronJobEntity> CreateProvider()
        {
            var dbContextFactory = new TestDbContextFactory(_options);
            var ownerIdentity = new TestOwnerIdentity();
            var schedulerOptions = new SchedulerOptionsBuilder();

            return new(
                dbContextFactory,
                _options,
                TimeProvider.System,
                new SequentialGuidGenerator(SequentialGuidType.Version7),
                ownerIdentity,
                schedulerOptions,
                cache: null,
                new EfCoreCasJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>(
                    dbContextFactory,
                    TimeProvider.System,
                    new SequentialGuidGenerator(SequentialGuidType.Version7),
                    ownerIdentity,
                    schedulerOptions
                ),
                NullLogger.Instance
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
        public JobsDbContext CreateDbContext()
        {
            return new(options);
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
