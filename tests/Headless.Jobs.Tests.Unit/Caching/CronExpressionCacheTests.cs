// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Caching;

public sealed class CronExpressionCacheTests : TestBase
{
    [Fact]
    public async Task GetAllCronJobExpressions_without_cache_reads_database()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("daily", "0 0 * * *"));
        var sut = fixture.CreateProvider();

        var result = await sut.GetAllCronJobExpressionsAsync(AbortToken);

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

        var result = await sut.GetAllCronJobExpressionsAsync(AbortToken);

        result.Should().ContainSingle().Which.Function.Should().Be("cached");
        cache.GetOrAddCalls.Should().Be(1);
        cache.FactoryCalls.Should().Be(0);
        cache.LastKey.Should().Be("jobs:cron:expressions");
        cache.LastOptions.Duration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task GetAllCronJobExpressions_cache_miss_loads_database_once()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("db", "0 */2 * * *"));
        var cache = new RecordingCache { Behavior = CacheBehavior.InvokeFactory };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.GetAllCronJobExpressionsAsync(AbortToken);

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

        var result = await sut.GetAllCronJobExpressionsAsync(AbortToken);

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

        var result = await sut.GetAllCronJobExpressionsAsync(AbortToken);

        result.Should().ContainSingle().Which.Function.Should().Be("db");
        cache.FactoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetAllCronJobExpressions_cache_hit_with_no_value_returns_empty_without_database()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("db", "0 9 * * *"));
        var cache = new RecordingCache { Behavior = CacheBehavior.ReturnNoValue };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.GetAllCronJobExpressionsAsync(AbortToken);

        // Contract (#6): a cache hit is authoritative. A NoValue hit collapses to [] and never re-queries the DB,
        // even though the DB has rows — providers must never persist a no-value cron entry.
        result.Should().BeEmpty();
        cache.FactoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetAllCronJobExpressions_cache_hit_with_null_value_returns_empty_without_database()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("db", "0 10 * * *"));
        var cache = new RecordingCache { Behavior = CacheBehavior.ReturnNullValue };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.GetAllCronJobExpressionsAsync(AbortToken);

        // Contract (#6): HasValue=true with Value=null also collapses to [] with no DB revalidation.
        result.Should().BeEmpty();
        cache.FactoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetAllCronJobExpressions_factory_failure_propagates_without_fallback()
    {
        var cache = new RecordingCache { Behavior = CacheBehavior.InvokeFactory };
        // The factory (DB load) itself throwing is a real load failure, not a cache-layer failure: factoryFailed
        // suppresses the fail-open path so the error surfaces rather than being masked as an empty cron set.
        // The coordinated-write options template is unused on this path (no coordinator); any valid options suffices.
        var coordinatedWriteOptions = new DbContextOptionsBuilder<JobsDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var sut = new JobsEfCorePersistenceProvider<JobsDbContext, TimeJobEntity, CronJobEntity>(
            new ThrowingDbContextFactory(),
            coordinatedWriteOptions,
            TimeProvider.System,
            new TestOwnerIdentity(),
            new SchedulerOptionsBuilder(),
            cache,
            NullLogger.Instance
        );

        var act = () => sut.GetAllCronJobExpressionsAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        cache.FactoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetAllCronJobExpressions_foreign_token_cache_cancellation_falls_back_to_database()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        await fixture.SeedCronJobsAsync(_Cron("db", "0 3 * * *"));
        // An OperationCanceledException bound to a foreign/internal token (e.g. a Redis command timeout), not the
        // caller's token, is a cache-layer failure and must fail open to the DB rather than propagate.
        var cache = new RecordingCache
        {
            Behavior = CacheBehavior.ThrowBeforeFactory,
            ReadException = new OperationCanceledException("cache store timeout"),
        };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.GetAllCronJobExpressionsAsync(AbortToken);

        result.Should().ContainSingle().Which.Function.Should().Be("db");
        cache.FactoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetAllCronJobExpressions_caller_cancellation_propagates()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        var cache = new RecordingCache
        {
            Behavior = CacheBehavior.ThrowBeforeFactory,
            ReadException = new OperationCanceledException("caller cancelled"),
        };
        var sut = fixture.CreateProvider(cache);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // The caller's own token is cancelled, so the cancellation is genuine and must propagate (no DB fallback).
        var act = () => sut.GetAllCronJobExpressionsAsync(cts.Token);

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

        await sut.InsertCronJobsAsync([_Cron("new", "0 7 * * *")], AbortToken);
        cronJob.Expression = "0 8 * * *";
        await sut.UpdateCronJobsAsync([cronJob], AbortToken);
        await sut.RemoveCronJobsAsync([cronJob.Id], AbortToken);

        cache.RemovedKeys.Should().Equal("jobs:cron:expressions", "jobs:cron:expressions", "jobs:cron:expressions");
    }

    [Fact]
    public async Task Cron_job_write_cache_invalidation_failure_is_best_effort()
    {
        await using var fixture = await CronCacheFixture.CreateAsync();
        var cache = new RecordingCache { RemoveException = new InvalidOperationException("cache remove failed") };
        var sut = fixture.CreateProvider(cache);

        var result = await sut.InsertCronJobsAsync([_Cron("new", "0 7 * * *")], AbortToken);

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
            await connection.OpenAsync(AbortToken);

            var services = new ServiceCollection()
                .AddEntityFrameworkSqlite()
                .AddSingleton(new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>())
                .BuildServiceProvider();

            var options = new DbContextOptionsBuilder<JobsDbContext>()
                .UseSqlite(connection)
                .UseApplicationServiceProvider(services)
                .Options;

            var fixture = new CronCacheFixture(connection, services, options);
            await using var dbContext = fixture._CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync(AbortToken);

            return fixture;
        }

        public JobsEfCorePersistenceProvider<JobsDbContext, TimeJobEntity, CronJobEntity> CreateProvider(
            ICache? cache = null
        ) =>
            new(
                new TestDbContextFactory(_options),
                _options,
                TimeProvider.System,
                new TestOwnerIdentity(),
                new SchedulerOptionsBuilder(),
                cache,
                NullLogger.Instance
            );

        public async Task SeedCronJobsAsync(params CronJobEntity[] cronJobs)
        {
            await using var dbContext = _CreateDbContext();
            await dbContext.Set<CronJobEntity>().AddRangeAsync(cronJobs, AbortToken);
            await dbContext.SaveChangesAsync(AbortToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
            await _services.DisposeAsync();
        }

        private JobsDbContext _CreateDbContext() => new(_options);
    }

    private sealed class TestDbContextFactory(DbContextOptions<JobsDbContext> options)
        : IDbContextFactory<JobsDbContext>
    {
        public JobsDbContext CreateDbContext() => new(options);
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<JobsDbContext>
    {
        public JobsDbContext CreateDbContext() => throw new InvalidOperationException("database unavailable");
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
        ReturnNoValue,
        ReturnNullValue,
    }

    private sealed class RecordingCache : ICache
    {
        public CacheEntryOptions? DefaultEntryOptions => null;

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

            if (Behavior == CacheBehavior.ReturnNoValue)
            {
                return CacheValue<T>.NoValue;
            }

            if (Behavior == CacheBehavior.ReturnNullValue)
            {
                return CacheValue<T>.Null;
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

        public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
            string key,
            Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions options,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

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

        public ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
        {
            RemoveCalls++;

            if (RemoveException is not null)
            {
                throw RemoveException;
            }

            RemovedKeys.Add(key);

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> UpsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<bool> UpsertEntryAsync<T>(
            string key,
            T? value,
            CacheEntryOptions options,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<int> UpsertAllAsync<T>(
            IDictionary<string, T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<bool> TryInsertAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<bool> TryReplaceAsync<T>(
            string key,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<bool> TryReplaceIfEqualAsync<T>(
            string key,
            T? expected,
            T? value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<double> IncrementAsync(
            string key,
            double amount,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<long> IncrementAsync(
            string key,
            long amount,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<double> SetIfHigherAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<long> SetIfHigherAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<double> SetIfLowerAsync(
            string key,
            double value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<long> SetIfLowerAsync(
            string key,
            long value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<long> SetAddAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
            IEnumerable<string> cacheKeys,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
            string prefix,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
            string prefix,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
            string key,
            int? pageIndex = null,
            int pageSize = 100,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> RemoveIfEqualAsync<T>(
            string key,
            T? expected,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<int> RemoveAllAsync(
            IEnumerable<string> cacheKeys,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<long> SetRemoveAsync<T>(
            string key,
            IEnumerable<T> value,
            TimeSpan? expiration,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
