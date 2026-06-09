// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Caching;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Caching;

public sealed class CronExpressionCacheTests
{
    [Fact]
    public async Task GetAllCronJobExpressions_without_cache_reads_database()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("daily", "0 0 * * *"));
        var sut = fixture.CreateProvider();

        var result = await sut.GetAllCronJobExpressions(TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Expression.Should().Be("0 0 * * *");
    }

    [Fact]
    public async Task GetAllCronJobExpressions_cache_hit_skips_database_factory()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        var cache = new RecordingCache
        {
            Behavior = CacheBehavior.ReturnCached,
            CachedCronExpressions = [_Cron("cached", "*/5 * * * *")],
        };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.GetAllCronJobExpressions(TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Function.Should().Be("cached");
        cache.GetOrAddCalls.Should().Be(1);
        cache.FactoryCalls.Should().Be(0);
        cache.LastKey.Should().Be("cron:expressions");
        cache.LastOptions.Duration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task GetAllCronJobExpressions_cache_miss_loads_database_once()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("db", "0 */2 * * *"));
        var cache = new RecordingCache { Behavior = CacheBehavior.InvokeFactory };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.GetAllCronJobExpressions(TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Function.Should().Be("db");
        cache.GetOrAddCalls.Should().Be(1);
        cache.FactoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetAllCronJobExpressions_cache_read_failure_falls_back_to_database()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("db", "0 12 * * *"));
        var cache = new RecordingCache { Behavior = CacheBehavior.ThrowBeforeFactory };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.GetAllCronJobExpressions(TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Function.Should().Be("db");
        cache.FactoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetAllCronJobExpressions_cache_write_failure_returns_database_result()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("db", "0 6 * * *"));
        var cache = new RecordingCache { Behavior = CacheBehavior.ThrowAfterFactory };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.GetAllCronJobExpressions(TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Function.Should().Be("db");
        cache.FactoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetAllCronJobExpressions_cache_cancellation_propagates()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        var cache = new RecordingCache
        {
            Behavior = CacheBehavior.ThrowBeforeFactory,
            ReadException = new OperationCanceledException("cache cancelled"),
        };
        var sut = fixture.CreateProvider(cache);

        var act = () => sut.GetAllCronJobExpressions(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cron_job_writes_invalidate_registered_cache()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        var cronJob = _Cron("db", "0 6 * * *");
        await fixture.SeedCronJobsAsync(cronJob);
        var cache = new RecordingCache();
        var sut = fixture.CreateProvider(cache);

        await sut.InsertCronJobs([_Cron("new", "0 7 * * *")], TestContext.Current.CancellationToken);
        cronJob.Expression = "0 8 * * *";
        await sut.UpdateCronJobs([cronJob], TestContext.Current.CancellationToken);
        await sut.RemoveCronJobs([cronJob.Id], TestContext.Current.CancellationToken);

        cache.RemovedKeys.Should().Equal("cron:expressions", "cron:expressions", "cron:expressions");
    }

    [Fact]
    public async Task Cron_job_write_cache_invalidation_failure_is_best_effort()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        var cache = new RecordingCache { RemoveException = new InvalidOperationException("cache remove failed") };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.InsertCronJobs([_Cron("new", "0 7 * * *")], TestContext.Current.CancellationToken);

        result.Should().Be(1);
        cache.RemoveCalls.Should().Be(1);
    }

    private static CronJobEntity _Cron(string function, string expression) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Description = function,
            Expression = expression,
            Request = [],
        };

    private sealed class CronCacheFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        private readonly DbContextOptions<JobsDbContext> _options;

        private CronCacheFixture(
            SqliteConnection connection,
            ServiceProvider services,
            DbContextOptions<JobsDbContext> options
        )
        {
            _connection = connection;
            _services = services;
            _options = options;
        }

        public static async Task<CronCacheFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            var services = new ServiceCollection()
                .AddEntityFrameworkSqlite()
                .AddSingleton(new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>())
                .BuildServiceProvider();

            var options = new DbContextOptionsBuilder<JobsDbContext>()
                .UseSqlite(connection)
                .UseApplicationServiceProvider(services)
                .Options;

            var fixture = new CronCacheFixture(connection, services, options);
            await using var dbContext = fixture.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            return fixture;
        }

        public JobsEfCorePersistenceProvider<JobsDbContext, TimeJobEntity, CronJobEntity> CreateProvider(
            ICache? cache = null
        ) => new(new TestDbContextFactory(_options), TimeProvider.System, new TestOwnerIdentity(), cache);

        public async Task SeedCronJobsAsync(params CronJobEntity[] cronJobs)
        {
            await using var dbContext = CreateDbContext();
            await dbContext.Set<CronJobEntity>().AddRangeAsync(cronJobs, TestContext.Current.CancellationToken);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
            await _services.DisposeAsync();
        }

        private JobsDbContext CreateDbContext() => new(_options);
    }

    private sealed class TestDbContextFactory(DbContextOptions<JobsDbContext> options)
        : IDbContextFactory<JobsDbContext>
    {
        public JobsDbContext CreateDbContext() => new(options);
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

    private enum CacheBehavior
    {
        ReturnCached,
        InvokeFactory,
        ThrowBeforeFactory,
        ThrowAfterFactory,
    }

    private sealed class RecordingCache : ICache
    {
        public CacheBehavior Behavior { get; init; } = CacheBehavior.InvokeFactory;

        public CronJobEntity[] CachedCronExpressions { get; init; } = [];

        public Exception ReadException { get; init; } = new InvalidOperationException("cache read failed");

        public Exception WriteException { get; init; } = new InvalidOperationException("cache write failed");

        public Exception? RemoveException { get; init; }

        public int GetOrAddCalls { get; private set; }

        public int FactoryCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public string? LastKey { get; private set; }

        public CacheEntryOptions LastOptions { get; private set; }

        public List<string> RemovedKeys { get; } = [];

        public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T?>> factory,
            CacheEntryOptions options,
            CancellationToken cancellationToken = default
        )
        {
            GetOrAddCalls++;
            LastKey = key;
            LastOptions = options;

            if (Behavior == CacheBehavior.ReturnCached)
            {
                return new CacheValue<T>((T?)(object?)CachedCronExpressions, hasValue: true);
            }

            if (Behavior == CacheBehavior.ThrowBeforeFactory)
            {
                throw ReadException;
            }

            FactoryCalls++;
            var value = await factory(cancellationToken).ConfigureAwait(false);

            if (Behavior == CacheBehavior.ThrowAfterFactory)
            {
                throw WriteException;
            }

            return new CacheValue<T>(value, hasValue: true);
        }

        public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            RemoveCalls++;

            if (RemoveException is not null)
            {
                throw RemoveException;
            }

            RemovedKeys.Add(key);

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> UpsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<int> UpsertAllAsync<T>(IDictionary<string, T> value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> TryInsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> TryReplaceAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> TryReplaceIfEqualAsync<T>(string key, T? expected, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<double> IncrementAsync(string key, double amount, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<long> IncrementAsync(string key, long amount, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<double> SetIfHigherAsync(string key, double value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<long> SetIfHigherAsync(string key, long value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<double> SetIfLowerAsync(string key, double value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<long> SetIfLowerAsync(string key, long value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<long> SetAddAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(string prefix, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(string key, int? pageIndex = null, int pageSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<long> SetRemoveAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
