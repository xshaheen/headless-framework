// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed class InMemoryInboxStorageConformanceTests : InboxStorageConformanceTests
{
    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        setup.UseInMemoryStorage();
    }
}
