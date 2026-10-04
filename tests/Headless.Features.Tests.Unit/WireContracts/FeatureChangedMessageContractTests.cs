// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Runtime;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestAssemblyMessagingModule = Headless.Features.Tests.Unit.MessagingModule;

namespace Tests.WireContracts;

/// <summary>
/// The feature-change announcement travels under a name <c>AddHeadlessFeatures</c> declares, so services sharing a
/// broker agree on the topic whatever naming conventions each host configures for its own messages, and an application
/// consumer subscribes to it with no declaration of its own.
/// </summary>
public sealed class FeatureChangedMessageContractTests : TestBase
{
    [Fact]
    public async Task should_publish_and_consume_under_the_declared_name_whatever_the_host_conventions()
    {
        // given - conventions that rename every message the host does not declare
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddHeadlessFeatures(setup => setup.UseEntityFramework<ContractTestDbContext>());
                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UseInMemoryStorage();
                    setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                    setup.UseConventions(static conventions =>
                        conventions
                            .UseKebabCaseMessageNames()
                            .WithMessageNamePrefix("app.")
                            .WithMessageNameSuffix(".event")
                    );
                });
                services.ConfigureMessaging(static messaging => messaging.AddModule<TestAssemblyMessagingModule>());
            },
            AbortToken
        );
        var message = new FeatureChangedMessage
        {
            FeatureNames = ["App.Reports"],
            ProviderName = FeatureValueProviderNames.Tenant,
            ProviderKey = "tenant-1",
            OriginHostName = "node-a",
        };

        // when
        await harness.Publisher.PublishAsync(message, cancellationToken: AbortToken);

        // then
        var published = await harness.WaitForPublished<FeatureChangedMessage>(cancellationToken: AbortToken);
        var consumed = await harness.WaitForConsumed<FeatureChangedMessage>(cancellationToken: AbortToken);
        published.MessageName.Should().Be("headless.features.changed");
        consumed.MessageName.Should().Be("headless.features.changed");
        harness
            .ServiceProvider.GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle(metadata => metadata.ConsumerType == typeof(AppFeatureChangedConsumer))
            .Which.MessageContractVersion.Should()
            .Be("1");
    }

    private sealed class ContractTestDbContext(DbContextOptions<ContractTestDbContext> options) : DbContext(options);
}

/// <summary>An application's consumer of the announcement, declared the way any host declares one.</summary>
[BusConsumer("tests.features.changed-watcher", EveryInstance = true)]
public sealed class AppFeatureChangedConsumer : IConsume<FeatureChangedMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<FeatureChangedMessage> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
