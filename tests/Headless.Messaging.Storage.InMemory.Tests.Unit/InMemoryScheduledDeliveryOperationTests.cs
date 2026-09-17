// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryScheduledDeliveryOperationTests : ScheduledDeliveryOperationConformanceTests
{
    protected override TimeProvider CreateHistoryClock() => new FakeTimeProvider(DateTimeOffset.UtcNow);

    protected override Task AgeHistoryAsync(ServiceProvider provider, TimeSpan age)
    {
        ((FakeTimeProvider)provider.GetRequiredService<TimeProvider>()).Advance(age);
        return Task.CompletedTask;
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        setup.UseInMemoryStorage();
    }
}
