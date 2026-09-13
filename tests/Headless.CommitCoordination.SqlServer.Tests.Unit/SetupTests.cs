// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests
{
    [Fact]
    public void should_register_the_core_services_once()
    {
        var services = new ServiceCollection();

        services.AddSqlServerCommitCoordination();
        services.AddSqlServerCommitCoordination();

        services.Count(d => d.ServiceType == typeof(ICommitScopeFactory)).Should().Be(1);
        services.Should().NotContain(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentCommitCoordinator>().Should().NotBeNull();
        provider.GetRequiredService<ICommitScopeFactory>().Should().NotBeNull();
    }
}
