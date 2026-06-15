// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests
{
    [Fact]
    public void should_register_postgresql_signal_source()
    {
        var services = new ServiceCollection();

        services.AddPostgreSqlCommitCoordination();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentCommitCoordinator>().Should().NotBeNull();
        provider.GetRequiredService<PostgreSqlCommitSignalSource>().Should().NotBeNull();
    }
}
