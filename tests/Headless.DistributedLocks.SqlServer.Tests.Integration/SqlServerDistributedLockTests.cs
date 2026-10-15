// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.SqlServer;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection<SqlServerDistributedLockFixture>]
public sealed class SqlServerDistributedLockTests(SqlServerDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_acquire_release_and_issue_monotonic_fencing_tokens()
    {
        await using var provider = _CreateProvider();

        // What host startup runs: the schema runner creates this key prefix's fence sequence before the first acquire.
        await provider.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken);
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var first = await locks.AcquireAsync(resource, cancellationToken: AbortToken);
        var firstToken = first.FencingToken;
        await first.ReleaseAsync();

        await using var second = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        firstToken.Should().NotBeNull();
        second.FencingToken!.Value.Should().BeGreaterThan(firstToken!.Value);
    }

    [Fact]
    public async Task should_issue_a_higher_token_to_the_next_holder_when_the_first_holders_session_is_killed()
    {
        // given
        var keyPrefix = $"sqlserver:{Faker.Random.AlphaNumeric(6)}:";
        var resource = Faker.Random.AlphaNumeric(12);
        await using var firstProvider = _CreateProvider(options => options.KeyPrefix = keyPrefix);
        await using var secondProvider = _CreateProvider(options => options.KeyPrefix = keyPrefix);
        await firstProvider.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken);
        var first = await firstProvider
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(resource, cancellationToken: AbortToken);

        // when (the server ends the first holder's session, which drops its application lock)
        await _KillAsync(await _SessionHoldingAsync(SqlServerResourceName.Encode(keyPrefix + resource)));
        await using var second = await secondProvider
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(10) },
                AbortToken
            );

        // then (a write fenced by the dead holder's token is rejected once the new holder's token is recorded)
        second.FencingToken!.Value.Should().BeGreaterThan(first.FencingToken!.Value);
    }

    [Fact]
    public async Task should_refuse_a_contended_fenced_acquire_without_consuming_a_token()
    {
        // given (the fenced grant batch takes its not-granted branch when another session holds the lock)
        var keyPrefix = $"sqlserver:{Faker.Random.AlphaNumeric(6)}:";
        var resource = Faker.Random.AlphaNumeric(12);
        await using var firstProvider = _CreateProvider(options => options.KeyPrefix = keyPrefix);
        await using var secondProvider = _CreateProvider(options => options.KeyPrefix = keyPrefix);
        await firstProvider.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken);
        var secondLocks = secondProvider.GetRequiredService<IDistributedLock>();
        var first = await firstProvider
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(resource, cancellationToken: AbortToken);
        var firstToken = first.FencingToken!.Value.Value;

        // when
        var refused = await secondLocks.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        await first.ReleaseAsync();
        await using var next = await secondLocks.AcquireAsync(resource, cancellationToken: AbortToken);

        // then (the key prefix has its own sequence, so a refused attempt that drew a value would make this first + 2)
        refused.Should().BeNull();
        next.FencingToken!.Value.Value.Should().Be(firstToken + 1);
    }

    [Fact]
    public async Task should_issue_no_token_when_fencing_is_disabled()
    {
        // given (no schema runner: with fencing off the acquire must not touch a fence sequence at all)
        await using var provider = _CreateProvider(options => options.EnableFencing = false);
        var locks = provider.GetRequiredService<IDistributedLock>();

        // when
        await using var handle = await locks.AcquireAsync(Faker.Random.AlphaNumeric(12), cancellationToken: AbortToken);

        // then
        handle.FencingToken.Should().BeNull();
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
    public async Task should_acquire_and_release_synchronously_through_db_transaction()
    {
        // The synchronous DbTransaction overload is the shape an EF Core SavingChanges interceptor holds.
        var resource = Faker.Random.AlphaNumeric(12);

        await using var holderConnection = await _OpenAsync();
        await using var holderTransaction = await holderConnection.BeginTransactionAsync(AbortToken);
        SqlServerDistributedLock.AcquireWithTransaction(resource, holderTransaction);

        await using var contenderConnection = await _OpenAsync();
        await using var contenderTransaction = await contenderConnection.BeginTransactionAsync(AbortToken);
        SqlServerDistributedLock
            .TryAcquireWithTransaction(resource, contenderTransaction, TimeSpan.Zero)
            .Should()
            .BeFalse();

        await holderTransaction.CommitAsync(AbortToken);

        SqlServerDistributedLock
            .TryAcquireWithTransaction(resource, contenderTransaction, TimeSpan.Zero)
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_succeed_synchronously_when_the_transaction_already_holds_the_lock()
    {
        // given — a SavingChanges interceptor runs once per save, so one transaction can ask for its lock twice
        var resource = Faker.Random.AlphaNumeric(12);

        await using var connection = await _OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        SqlServerDistributedLock.AcquireWithTransaction(resource, transaction);

        // when
        var act = () => SqlServerDistributedLock.AcquireWithTransaction(resource, transaction, TimeSpan.FromSeconds(1));

        // then
        act.Should().NotThrow();
        SqlServerDistributedLock.TryAcquireWithTransaction(resource, transaction, TimeSpan.Zero).Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_when_the_transaction_already_holds_the_lock()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(12);

        await using var connection = await _OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);
        await SqlServerDistributedLock.AcquireWithTransactionAsync(
            resource,
            transaction,
            cancellationToken: AbortToken
        );

        // when — the transaction is the owner, so a second acquire is granted on every wait shape
        await SqlServerDistributedLock.AcquireWithTransactionAsync(
            resource,
            transaction,
            Timeout.InfiniteTimeSpan,
            cancellationToken: AbortToken
        );
        var acquiredAgain = await SqlServerDistributedLock.TryAcquireWithTransactionAsync(
            resource,
            transaction,
            TimeSpan.Zero,
            cancellationToken: AbortToken
        );

        // then — and one commit releases it
        acquiredAgain.Should().BeTrue();
        await transaction.CommitAsync(AbortToken);
        (await _CanTakeTransactionLockAsync(resource)).Should().BeTrue();
    }

    [Fact]
    public async Task should_surface_a_cancelled_transaction_acquire_as_cancellation_and_keep_the_transaction_usable()
    {
        // given — another session holds the resource, so the transaction-owned acquire waits server-side
        var resource = Faker.Random.AlphaNumeric(12);
        await using var holderConnection = await _HoldSessionLockAsync(resource);

        await using var connection = await _OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        // when
        var act = async () =>
            await SqlServerDistributedLock.TryAcquireWithTransactionAsync(
                resource,
                transaction,
                TimeSpan.FromSeconds(30),
                cancellationToken: cancellation.Token
            );

        // then — SqlClient's in-flight cancel is reported as cancellation, the transaction owns no lock, and the
        // transaction stays committable
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await _LockModeAsync(connection, transaction, resource, "Transaction")).Should().Be("NoLock");
        (await _TransactionStateAsync(connection, transaction)).Should().Be(1);
        await transaction.CommitAsync(AbortToken);
    }

    [Fact]
    public async Task should_surface_a_cancellation_requested_before_the_session_acquire_starts()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(12);
        await using var connection = await _OpenAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // when
        var act = async () =>
            await SqlServerApplicationLock.TryAcquireSessionAsync(
                connection,
                _Encode(resource),
                isShared: false,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(30),
                fenceSequence: null,
                cancellation.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await _LockModeAsync(connection, transaction: null, resource, "Session")).Should().Be("NoLock");
    }

    [Fact]
    public async Task should_propagate_a_killed_session_without_attempting_a_release()
    {
        // given — the transaction-owned acquire waits behind another session's hold
        var resource = Faker.Random.AlphaNumeric(12);
        await using var holderConnection = await _HoldSessionLockAsync(resource);

        await using var connection = await _OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);
        var sessionId = await _SessionIdAsync(connection, transaction);

        // when — the server ends the waiting session, which takes its locks with it
        var acquire = Task.Run(
            () =>
                SqlServerApplicationLock.TryAcquireTransaction(
                    transaction,
                    _Encode(resource),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30)
                ),
            AbortToken
        );
        await _WaitUntilBlockedAsync(sessionId);
        await _KillAsync(sessionId);

        // then — the connection failure itself is reported
        await acquire.Awaiting(x => x).Should().ThrowAsync<SqlException>();
        connection.State.Should().NotBe(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task should_surface_a_cancelled_session_acquire_as_cancellation_and_hold_nothing()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(12);
        await using var holderConnection = await _HoldSessionLockAsync(resource);

        await using var connection = await _OpenAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        // when
        var act = async () =>
            await SqlServerApplicationLock.TryAcquireSessionAsync(
                connection,
                _Encode(resource),
                isShared: false,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                fenceSequence: null,
                cancellation.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await _LockModeAsync(connection, transaction: null, resource, "Session")).Should().Be("NoLock");
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

    [Fact]
    public async Task should_release_cleanly_when_a_liveness_probe_owns_the_connection_gate()
    {
        var timeProvider = new FakeTimeProvider();
        var probeOwnsGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var storage = new SqlServerConnectionScopedLockStorage(
            Options.Create(
                new SqlServerDistributedLockOptions
                {
                    ConnectionString = fixture.ConnectionString,
                    KeyPrefix = $"sqlserver:{Faker.Random.AlphaNumeric(6)}:",
                    // No schema runner here, so no fence sequence: this test is about the probe gate, not tokens.
                    EnableFencing = false,
                }
            ),
            Options.Create(new DistributedLocksStorageOptions()),
            timeProvider
        )
        {
            ProbeGateAcquiredAsync = async () =>
            {
                probeOwnsGate.TrySetResult();
                await resumeProbe.Task.WaitAsync(AbortToken);
            },
        };
        var resource = Faker.Random.AlphaNumeric(12);
        var leaseId = Guid.NewGuid().ToString("N");
        var handle = await storage.TryAcquireAsync(resource, leaseId, isShared: false, observeLoss: true, AbortToken);
        handle.Should().NotBeNull();

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        await probeOwnsGate.Task.WaitAsync(AbortToken);

        var release = handle!.ReleaseAsync(AbortToken).AsTask();
        var releaseWaitedForProbe = !release.IsCompleted;

        resumeProbe.TrySetResult();
        await release.WaitAsync(AbortToken);

        releaseWaitedForProbe.Should().BeTrue();
        handle.ConnectionLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_free_the_application_lock_when_its_release_throws()
    {
        // given (a release that fails before it reaches the server leaves the lock held on its session)
        await using var storage = new SqlServerConnectionScopedLockStorage(
            Options.Create(
                new SqlServerDistributedLockOptions
                {
                    ConnectionString = fixture.ConnectionString,
                    KeyPrefix = DistributedLockOptions.DefaultKeyPrefix,
                    EnableFencing = false,
                }
            ),
            Options.Create(new DistributedLocksStorageOptions()),
            TimeProvider.System
        )
        {
            BeforeReleaseAsync = () => throw new InvalidOperationException("Simulated release failure."),
        };
        var resource = Faker.Random.AlphaNumeric(12);
        var handle = await storage.TryAcquireAsync(
            resource,
            Guid.NewGuid().ToString("N"),
            isShared: false,
            observeLoss: false,
            AbortToken
        );
        handle.Should().NotBeNull();

        // when
        var release = async () => await handle!.ReleaseAsync(AbortToken);

        // then (the session is ended rather than pooled, so the lock does not outlive the failed release)
        await release.Should().ThrowAsync<InvalidOperationException>();
        (await _WaitUntilSessionLockFreeAsync(resource, TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    [Fact]
    public async Task should_free_the_application_lock_when_acquire_cleanup_fails()
    {
        // given (the server granted the lock, then the acquire failed and so did its cleanup release)
        await using var storage = new SqlServerConnectionScopedLockStorage(
            Options.Create(
                new SqlServerDistributedLockOptions
                {
                    ConnectionString = fixture.ConnectionString,
                    KeyPrefix = DistributedLockOptions.DefaultKeyPrefix,
                    EnableFencing = false,
                }
            ),
            Options.Create(new DistributedLocksStorageOptions()),
            TimeProvider.System
        )
        {
            AfterAcquireSessionAsync = () =>
                throw new LockCleanupFailedException(
                    [
                        new TimeoutException("Simulated acquire failure."),
                        new TimeoutException("Simulated cleanup failure."),
                    ],
                    "Simulated cleanup failure."
                ),
        };
        var resource = Faker.Random.AlphaNumeric(12);

        // when
        var acquire = async () =>
            await storage.TryAcquireAsync(
                resource,
                Guid.NewGuid().ToString("N"),
                isShared: false,
                observeLoss: false,
                AbortToken
            );

        // then (the session is ended rather than pooled, so the lock does not outlive the failed cleanup)
        await acquire.Should().ThrowAsync<LockCleanupFailedException>();
        (await _WaitUntilSessionLockFreeAsync(resource, TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    private async Task<bool> _WaitUntilSessionLockFreeAsync(string resource, TimeSpan within)
    {
        var started = TimeProvider.System.GetTimestamp();

        while (TimeProvider.System.GetElapsedTime(started) < within)
        {
            if (await _CanTakeExclusiveSessionLockAsync(resource))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);
        }

        return await _CanTakeExclusiveSessionLockAsync(resource);
    }

    private async Task<bool> _CanTakeExclusiveSessionLockAsync(string resource)
    {
        // Unpooled, so the probe can never be handed the pooled session that holds the lock: APPLOCK_TEST on the owning
        // session reports the lock as available.
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString) { Pooling = false };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT APPLOCK_TEST(N'public', @resource, N'Exclusive', N'Session');";
        command.Parameters.AddWithValue("resource", _Encode(resource));

        return Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture) == 1;
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

    private static string _Encode(string resource)
    {
        // The static helpers default to DefaultKeyPrefix, so a raw app lock on this name contends with them.
        return SqlServerResourceName.Encode(DistributedLockOptions.DefaultKeyPrefix + resource);
    }

    private async Task<SqlConnection> _HoldSessionLockAsync(string resource)
    {
        var connection = await _OpenAsync();
        var (acquired, _) = await SqlServerApplicationLock.TryAcquireSessionAsync(
            connection,
            _Encode(resource),
            isShared: false,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(30),
            fenceSequence: null,
            AbortToken
        );
        acquired.Should().BeTrue();

        return connection;
    }

    private async Task<bool> _CanTakeTransactionLockAsync(string resource)
    {
        await using var connection = await _OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);

        return await SqlServerDistributedLock.TryAcquireWithTransactionAsync(
            resource,
            transaction,
            TimeSpan.Zero,
            cancellationToken: AbortToken
        );
    }

    private static async Task<string> _LockModeAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string resource,
        string owner
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT APPLOCK_MODE(N'public', @resource, @owner)";
        command.Parameters.AddWithValue(nameof(resource), _Encode(resource));
        command.Parameters.AddWithValue(nameof(owner), owner);

        return (string)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private static async Task<short> _SessionIdAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT CAST(@@SPID AS smallint)";

        return (short)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task _WaitUntilBlockedAsync(short sessionId)
    {
        await using var connection = await _OpenAsync();

        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = @sessionId AND blocking_session_id <> 0";
            command.Parameters.AddWithValue(nameof(sessionId), sessionId);

            if ((int)(await command.ExecuteScalarAsync(AbortToken))! > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        }
    }

    private async Task<short> _SessionHoldingAsync(string encodedResource)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();

        // dm_tran_locks shows at most the first 32 characters of an application lock name; the random key prefix keeps
        // that fragment unique to this test.
        command.CommandText = """
            SELECT TOP (1) CAST(request_session_id AS smallint)
            FROM sys.dm_tran_locks
            WHERE resource_type = N'APPLICATION'
              AND request_owner_type = N'SESSION'
              AND request_status = N'GRANT'
              AND resource_description LIKE N'%' + @fragment + N'%'
            """;
        command.Parameters.AddWithValue("fragment", encodedResource[..Math.Min(encodedResource.Length, 32)]);

        var sessionId = await command.ExecuteScalarAsync(AbortToken);
        sessionId.Should().NotBeNull("the first holder's session must be found");

        return (short)sessionId!;
    }

    private async Task _KillAsync(short sessionId)
    {
        await using var connection = await _OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = string.Create(CultureInfo.InvariantCulture, $"KILL {sessionId}");
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private static async Task<int> _TransactionStateAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT CAST(XACT_STATE() AS int)";

        return (int)(await command.ExecuteScalarAsync(AbortToken))!;
    }
}
