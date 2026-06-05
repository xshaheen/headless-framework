// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.Postgres;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class PostgresDistributedLockSetupTests : TestBase
{
    [Fact]
    public async Task should_register_mutex_and_reader_writer_providers()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddPostgresDistributedLocks(options =>
        {
            options.ConnectionString = "Host=localhost;Database=headless";
            options.EnablePushWakeup = false;
        });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDistributedLock>().Should().BeOfType<ConnectionScopedDistributedLock>();
        provider.GetRequiredService<IDistributedReadWriteLock>().Should().BeOfType<ConnectionScopedReadWriteLock>();
    }
}
