// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public async Task should_not_enable_transactional_outbox_on_raw_path()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
            setup.UseSqlServer("Server=localhost;Database=test;TrustServerCertificate=True")
        );

        await using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<ICurrentCommitCoordinator>()
            .GetType()
            .Name.Should()
            .Be("MessagingNullCommitCoordinator");
    }
}
