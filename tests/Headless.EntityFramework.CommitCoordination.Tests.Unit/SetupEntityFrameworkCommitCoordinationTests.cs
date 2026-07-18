// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupEntityFrameworkCommitCoordinationTests : TestBase
{
    [Fact]
    public void should_keep_commit_coordination_out_of_the_core_registration_by_default()
    {
        var services = new ServiceCollection();

        services.AddHeadlessDbContextServices();
        using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<IHeadlessTransactionCoordinator>()
            .Should()
            .BeOfType<NullHeadlessTransactionCoordinator>();
    }

    [Fact]
    public void should_replace_the_core_transaction_seam_when_selected()
    {
        var services = new ServiceCollection();

        services.AddHeadlessDbContextServices().AddCommitCoordination();
        using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<IHeadlessTransactionCoordinator>()
            .GetType()
            .Name.Should()
            .Be("HeadlessCommitCoordinationTransactionCoordinator");
    }
}
