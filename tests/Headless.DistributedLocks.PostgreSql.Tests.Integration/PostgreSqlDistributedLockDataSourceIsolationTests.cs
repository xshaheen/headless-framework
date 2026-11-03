// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting.Schema;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

/// <summary>
/// A host usually registers its own <see cref="NpgsqlDataSource"/> for application data. The lock provider must keep
/// using the database its own options name, so services that share one lock database still exclude each other.
/// </summary>
[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgreSqlDistributedLockDataSourceIsolationTests(PostgreSqlDistributedLockFixture fixture)
    : TestBase
{
    [Fact]
    public async Task should_lock_in_configured_database_when_app_registered_its_own_data_source_first()
    {
        // given - the application's data source points at a server that refuses connections
        await using var appDataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=app;Timeout=2");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(appDataSource);
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = $"isolation:{Faker.Random.AlphaNumeric(6)}:";
                options.EnablePushWakeup = false;
            })
        );
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken);

        // when
        var handle = await provider
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(
                Faker.Random.AlphaNumeric(12),
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                AbortToken
            );

        // then
        handle.FencingToken.Should().NotBeNull();
        await handle.ReleaseAsync();
        provider.GetRequiredService<NpgsqlDataSource>().Should().BeSameAs(appDataSource);
    }
}
