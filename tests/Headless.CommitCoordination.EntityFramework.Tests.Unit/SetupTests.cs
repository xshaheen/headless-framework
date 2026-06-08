// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests
{
    [Fact]
    public void should_register_entity_framework_signal_source()
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkCommitCoordination();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentCommitCoordinator>().Should().NotBeNull();
        provider.GetRequiredService<EntityFrameworkCommitSignalSource>().Should().NotBeNull();
    }
}
