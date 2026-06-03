// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.Postgres;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresDistributedLockTests(PostgresDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_acquire_release_and_issue_monotonic_fencing_tokens()
    {
        await using var provider = _CreateProvider();
        var locks = provider.GetRequiredService<IDistributedLockProvider>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var first = await locks.AcquireAsync(resource, cancellationToken: AbortToken);
        var firstToken = first.FencingToken;
        await first.ReleaseAsync();

        await using var second = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        firstToken.Should().NotBeNull();
        second.FencingToken.Should().BeGreaterThan(firstToken!.Value);
    }

    [Fact]
    public async Task should_return_null_expiration_for_session_scoped_lock()
    {
        await using var provider = _CreateProvider();
        var locks = provider.GetRequiredService<IDistributedLockProvider>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        (await locks.GetExpirationAsync(resource, AbortToken)).Should().BeNull();
        handle.IsMonitored.Should().BeTrue();
    }

    private ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddPostgresDistributedLocks(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.KeyPrefix = "test:";
        });

        return services.BuildServiceProvider();
    }
}
