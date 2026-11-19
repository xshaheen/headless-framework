// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Sql.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Proves the feature-owned <c>ConfigureStorage(o =&gt; o.Schema = …)</c> setting reaches the SQL Server fencing
/// sequence now that the setting moved off <c>SqlServerDistributedLockOptions</c>.
/// </summary>
[Collection<SqlServerDistributedLockFixture>]
public sealed class SqlServerCustomSchemaTests(SqlServerDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_the_fencing_sequence_in_the_configured_schema()
    {
        var schema = $"locks_{Faker.Random.AlphaNumeric(8).ToLowerInvariant()}";

        await using var provider = _BuildProvider(schema);
        await _StartAsync(provider);
        var locks = provider.GetRequiredService<IDistributedLock>();

        var handle = await locks.AcquireAsync(
            Faker.Random.AlphaNumeric(12),
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );

        handle.FencingToken.Should().NotBeNull();
        await handle.ReleaseAsync();

        var sequences = await _ListSequencesAsync(schema);

        sequences.Should().ContainSingle();
    }

    [Fact]
    public async Task should_default_to_the_shared_headless_schema_when_storage_is_not_configured()
    {
        await using var provider = _BuildProvider(schema: null);
        await _StartAsync(provider);
        var locks = provider.GetRequiredService<IDistributedLock>();

        var handle = await locks.AcquireAsync(
            Faker.Random.AlphaNumeric(12),
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );

        handle.FencingToken.Should().NotBeNull();
        await handle.ReleaseAsync();

        var sequences = await _ListSequencesAsync(HeadlessStorageDefaults.Schema);

        sequences.Should().NotBeEmpty();
    }

    [Fact]
    public async Task should_not_block_fencing_initialization_on_an_application_lock_named_like_the_init_lock()
    {
        // given — the application holds a lock whose resource spells the schema runner's lock as it would appear
        // inside the application's KeyPrefix namespace. The runner's lock lives outside that namespace, so it cannot
        // collide.
        var schema = $"locks_{Faker.Random.AlphaNumeric(8).ToLowerInvariant()}";
        var keyPrefix = $"init-lock:{Faker.Random.AlphaNumeric(6)}:";
        await using var identityConnection = new SqlConnection(fixture.ConnectionString);
        var runnerLock =
            $"headless_schema_runner:{SqlServerSchemaDialect.Instance.DatabaseIdentity(identityConnection)}";
        await using var holderProvider = _BuildProvider(schema, keyPrefix, enableFencing: false);
        await using var fencedProvider = _BuildProvider(schema, keyPrefix);
        await using var held = await holderProvider
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(runnerLock, cancellationToken: AbortToken);

        // when — the runner polls its lock without waiting, so a collision would not show as a lock wait: it would
        // spin until the runner's lock timeout. Probing the runner's exact resource shows the collision directly,
        // whatever the server's load, and the run that follows proves initialization and fencing then succeed.
        (await _TryTakeApplicationLockAsync(runnerLock))
            .Should()
            .BeTrue("the application's look-alike lock lives inside its KeyPrefix namespace, not on the runner's lock");

        await _StartAsync(fencedProvider);
        await using var first = await fencedProvider
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(Faker.Random.AlphaNumeric(12), cancellationToken: AbortToken);
        await using var second = await fencedProvider
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(Faker.Random.AlphaNumeric(12), cancellationToken: AbortToken);

        // then
        first.FencingToken.Should().NotBeNull();
        second.FencingToken!.Value.Should().BeGreaterThan(first.FencingToken!.Value);
    }

    // Takes and releases the application lock on exactly the resource the schema runner locks, without waiting, the
    // way the runner's own acquire does. False when another session holds it.
    private async Task<bool> _TryTakeApplicationLockAsync(string resource)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Session',
                @LockTimeout = 0,
                @DbPrincipal = N'public';
            IF @result >= 0
                EXEC sys.sp_releaseapplock @Resource = @resource, @LockOwner = N'Session', @DbPrincipal = N'public';
            SELECT CASE WHEN @result >= 0 THEN 1 ELSE 0 END;
            """;
        command.Parameters.AddWithValue("resource", resource);

        return (int)(await command.ExecuteScalarAsync(AbortToken))! == 1;
    }

    // What host startup runs: the schema runner creates the key prefix's fence sequence before the first acquire.
    private static Task _StartAsync(ServiceProvider provider)
    {
        return provider.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken);
    }

    private ServiceProvider _BuildProvider(string? schema, string? keyPrefix = null, bool enableFencing = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
        {
            if (schema is not null)
            {
                setup.ConfigureStorage(storage => storage.Schema = schema);
            }

            setup.UseSqlServer(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = keyPrefix ?? $"custom-schema:{Faker.Random.AlphaNumeric(6)}:";
                options.EnableFencing = enableFencing;
            });
        });

        return services.BuildServiceProvider();
    }

    private async Task<IReadOnlyList<string>> _ListSequencesAsync(string schema)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT q.name
            FROM sys.sequences q
            JOIN sys.schemas s ON s.schema_id = q.schema_id
            WHERE s.name = @schema;
            """;
        command.Parameters.AddWithValue(nameof(schema), schema);

        var sequences = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        while (await reader.ReadAsync(AbortToken))
        {
            sequences.Add(reader.GetString(0));
        }

        return sequences;
    }
}
