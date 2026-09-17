// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
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
        {
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.UseSqlServer("Server=localhost;Database=test;TrustServerCertificate=True");
        });

        await using var provider = services.BuildServiceProvider();

        // The raw (non-EF) path never enables the transactional inbox runner — that only exists when
        // UseEntityFramework<TContext>() wires an EF-backed transaction boundary.
        provider.GetService<IInboxTransactionRunner>().Should().BeNull();
    }
}
