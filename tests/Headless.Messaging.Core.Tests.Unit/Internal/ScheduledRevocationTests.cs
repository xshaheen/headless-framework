// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class ScheduledRevocationTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_never_send_revoked_schedule_even_when_dispatcher_retains_candidate(bool claimed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.Bus.ForMessage<Reminder>(message => message.Contract("test.revocation"));
        });
        await using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IDataStorage>();
        var receipt = await provider
            .GetRequiredService<IBus>()
            .PublishAsync(
                new Reminder(),
                new PublishOptions { ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(90) },
                AbortToken
            );
        receipt.StorageId.Should().NotBeNull();
        var candidates = await ((IDelayedMessageClaimStorage)storage).ClaimDelayedMessagesAsync(AbortToken);
        var candidate = candidates.Single(x => x.StorageId == receipt.StorageId);
        if (!claimed)
        {
            // Simulate a queued candidate without a store ownership grant, which takes the
            // sender's combined lease-and-reserve path rather than its claimed-row path.
            candidate.LockedUntil = null;
            candidate.Owner = null;
        }

        (await provider.GetRequiredService<IMessageRevoker>().RevokeAsync(receipt.StorageId.Value, AbortToken))
            .Should()
            .Be(MessageRevocationResult.Revoked);

        var transport = Substitute.For<IBusTransport>();
        transport.BrokerAddress.Returns(new BrokerAddress("test", "localhost"));
        var senderServices = new ServiceCollection();
        senderServices.AddLogging();
        senderServices.AddSingleton(storage);
        senderServices.AddSingleton(provider.GetRequiredService<ISerializer>());
        senderServices.AddSingleton(provider.GetRequiredService<IOptions<MessagingOptions>>());
        senderServices.AddSingleton(TimeProvider.System);
        senderServices.AddSingleton(transport);
        await using var senderProvider = senderServices.BuildServiceProvider();
        var sender = new MessageSender(senderProvider.GetRequiredService<ILogger<MessageSender>>(), senderProvider);
        await sender.SendAsync(candidate);

        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, AbortToken);
        await storage.ChangePublishStateToDelayedAsync([candidate.StorageId], AbortToken);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(candidate.StorageId, AbortToken)).Should().BeNull();
    }

    private sealed record Reminder;
}
