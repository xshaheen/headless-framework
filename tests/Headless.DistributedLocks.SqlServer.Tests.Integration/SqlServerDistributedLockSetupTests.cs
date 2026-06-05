// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SqlServerDistributedLockSetupTests : TestBase
{
    [Fact]
    public async Task should_register_mutex_and_reader_writer_providers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerDistributedLocks(options =>
        {
            options.ConnectionString = "Server=localhost;Database=headless;User Id=sa;Password=Password1!";
            options.EnableFencing = false;
        });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDistributedLockProvider>().Should().BeOfType<ConnectionScopedDistributedLockProvider>();
        provider.GetRequiredService<IDistributedReaderWriterLockProvider>()
            .Should()
            .BeOfType<ConnectionScopedReaderWriterLockProvider>();
        provider.GetRequiredService<IConnectionScopedLockStorage>().BlocksServerSide.Should().BeTrue();
    }
}
