// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;

namespace Tests;

public sealed class InMemoryInboxOperationPolicyTests : InboxOperationPolicyConformanceTests
{
    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        setup.UseInMemoryStorage();
    }
}
