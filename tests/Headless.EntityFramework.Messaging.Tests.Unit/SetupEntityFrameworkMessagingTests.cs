// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.EntityFramework;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SetupEntityFrameworkMessagingTests : TestBase
{
    [Fact]
    public void should_preserve_provider_neutral_integration_event_outbox_root()
    {
        typeof(SetupEntityFrameworkMessaging).Name.Should().Be("SetupEntityFrameworkMessaging");
        typeof(SetupEntityFrameworkMessaging)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Should()
            .ContainSingle(method => method.Name == "AddIntegrationEventOutbox");
    }
}
