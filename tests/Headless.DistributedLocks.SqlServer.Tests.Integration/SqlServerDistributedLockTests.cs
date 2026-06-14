// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqlServerDistributedLockFixture>]
public sealed class SqlServerDistributedLockTests(SqlServerDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_acquire_release_and_issue_monotonic_fencing_tokens()
    {
        await using var provider = _CreateProvider();
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var first = await locks.AcquireAsync(resource, cancellationToken: AbortToken);
        var firstToken = first.FencingToken;
        await first.ReleaseAsync();

        await using var second = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        firstToken.Should().NotBeNull();
        second.FencingToken.Should().BeGreaterThan(firstToken!.Value);
    }

    [Fact]
    public async Task should_enforce_shared_and_exclusive_reader_writer_modes()
    {
        await using var provider = _CreateProvider(options => options.EnableFencing = false);
        var locks = provider.GetRequiredService<IDistributedReadWriteLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using (var firstReader = await locks.AcquireReadLockAsync(resource, cancellationToken: AbortToken))
        await using (var secondReader = await locks.AcquireReadLockAsync(resource, cancellationToken: AbortToken))
        {
            (await locks.GetReaderCountAsync(resource, AbortToken)).Should().Be(2);

            var writer = await locks.TryAcquireWriteLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                AbortToken
            );

            writer.Should().BeNull();
        }

        await using var acquiredWriter = await locks.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);
        acquiredWriter.Should().NotBeNull();
    }

    [Fact]
    public async Task should_release_transaction_lock_on_commit()
    {
        var resource = Faker.Random.AlphaNumeric(12);

        await using var holderConnection = await _OpenAsync();
        await using var holderTransaction = (SqlTransaction)await holderConnection.BeginTransactionAsync(AbortToken);
        await SqlServerDistributedLock.AcquireWithTransactionAsync(
            resource,
            holderTransaction,
            cancellationToken: AbortToken
        );

        await using var contenderConnection = await _OpenAsync();
        await using var contenderTransaction = (SqlTransaction)
            await contenderConnection.BeginTransactionAsync(AbortToken);

        (
            await SqlServerDistributedLock.TryAcquireWithTransactionAsync(
                resource,
                contenderTransaction,
                TimeSpan.Zero,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeFalse();

        await holderTransaction.CommitAsync(AbortToken);

        await using var nextConnection = await _OpenAsync();
        await using var nextTransaction = (SqlTransaction)await nextConnection.BeginTransactionAsync(AbortToken);

        (
            await SqlServerDistributedLock.TryAcquireWithTransactionAsync(
                resource,
                nextTransaction,
                TimeSpan.Zero,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_hash_long_resource_names_and_acquire_successfully()
    {
        await using var provider = _CreateProvider(options => options.EnableFencing = false);
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = new string('x', SqlServerDistributedLockFieldLimits.MaxResourceNameLength + 100);

        await using var handle = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        handle.Resource.Should().Be(resource);
        (await locks.IsLockedAsync(resource, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_report_database_lock_held_by_separate_provider()
    {
        var keyPrefix = $"sqlserver:{Faker.Random.AlphaNumeric(6)}:";
        await using var firstProvider = _CreateProvider(options =>
        {
            options.EnableFencing = false;
            options.KeyPrefix = keyPrefix;
        });
        await using var secondProvider = _CreateProvider(options =>
        {
            options.EnableFencing = false;
            options.KeyPrefix = keyPrefix;
        });
        var firstLocks = firstProvider.GetRequiredService<IDistributedLock>();
        var secondLocks = secondProvider.GetRequiredService<IDistributedLock>();
        var secondReaderWriterLocks = secondProvider.GetRequiredService<IDistributedReadWriteLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await firstLocks.AcquireAsync(resource, cancellationToken: AbortToken);

        (await secondLocks.IsLockedAsync(resource, AbortToken)).Should().BeTrue();
        (await secondReaderWriterLocks.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_report_database_read_lock_held_by_separate_provider()
    {
        var keyPrefix = $"sqlserver:{Faker.Random.AlphaNumeric(6)}:";
        await using var firstProvider = _CreateProvider(options =>
        {
            options.EnableFencing = false;
            options.KeyPrefix = keyPrefix;
        });
        await using var secondProvider = _CreateProvider(options =>
        {
            options.EnableFencing = false;
            options.KeyPrefix = keyPrefix;
        });
        var firstLocks = firstProvider.GetRequiredService<IDistributedReadWriteLock>();
        var secondLocks = secondProvider.GetRequiredService<IDistributedReadWriteLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await firstLocks.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        (await secondLocks.IsReadLockedAsync(resource, AbortToken)).Should().BeTrue();

        // Cross-process reader counts are presence-only on SQL Server: APPLOCK_TEST reports the lock mode but not a
        // holder count, so a remotely-held read lock reads as 1 regardless of how many remote readers hold it. This
        // asserts presence (held by another process), not an exact remote count.
        (await secondLocks.GetReaderCountAsync(resource, AbortToken))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_mutually_exclude_provider_and_transaction_apis_on_same_resource()
    {
        var keyPrefix = $"sqlserver:{Faker.Random.AlphaNumeric(6)}:";
        await using var provider = _CreateProvider(options =>
        {
            options.EnableFencing = false;
            options.KeyPrefix = keyPrefix;
        });
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        // Hold the lock via the provider (session-scoped, KeyPrefix-encoded).
        await using var providerLock = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        // A same-resource transaction acquire must observe the conflict because both APIs encode KeyPrefix + resource.
        await using var contenderConnection = await _OpenAsync();
        await using var contenderTransaction = (SqlTransaction)
            await contenderConnection.BeginTransactionAsync(AbortToken);

        var acquired = await SqlServerDistributedLock.TryAcquireWithTransactionAsync(
            resource,
            contenderTransaction,
            TimeSpan.Zero,
            keyPrefix: keyPrefix,
            cancellationToken: AbortToken
        );

        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task should_report_transaction_owned_lock_as_locked_from_separate_connection()
    {
        var keyPrefix = $"sqlserver:{Faker.Random.AlphaNumeric(6)}:";
        await using var provider = _CreateProvider(options =>
        {
            options.EnableFencing = false;
            options.KeyPrefix = keyPrefix;
        });
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var holderConnection = await _OpenAsync();
        await using var holderTransaction = (SqlTransaction)await holderConnection.BeginTransactionAsync(AbortToken);
        await SqlServerDistributedLock.AcquireWithTransactionAsync(
            resource,
            holderTransaction,
            keyPrefix: keyPrefix,
            cancellationToken: AbortToken
        );

        // The probe (APPLOCK_TEST on a separate connection) must see the transaction-owned exclusive lock as a
        // conflict, even though it is held under the Transaction owner.
        (await locks.IsLockedAsync(resource, AbortToken))
            .Should()
            .BeTrue();
    }

    private ServiceProvider _CreateProvider(Action<SqlServerDistributedLockOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UseSqlServer(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = $"sqlserver:{Faker.Random.AlphaNumeric(6)}:";
                configure?.Invoke(options);
            })
        );

        return services.BuildServiceProvider();
    }

    private async Task<SqlConnection> _OpenAsync()
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }
}
