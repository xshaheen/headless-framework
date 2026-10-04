// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Runtime;
using Headless.Messaging.Testing;
using Headless.Security;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestAssemblyMessagingModule = Headless.Settings.Tests.Unit.MessagingModule;

namespace Tests.WireContracts;

/// <summary>
/// The setting-change announcement travels under a name <c>AddHeadlessSettings</c> declares, so services sharing a
/// broker agree on the topic whatever naming conventions each host configures for its own messages, and an application
/// consumer subscribes to it with no declaration of its own.
/// </summary>
public sealed class SettingChangedMessageContractTests : TestBase
{
    [Fact]
    public async Task should_publish_and_consume_under_the_declared_name_whatever_the_host_conventions()
    {
        // given - conventions that rename every message the host does not declare
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddStringEncryptionService(options =>
                {
                    options.DefaultPassPhrase = "TestPassPhrase123456";
                    options.DefaultSalt = [.. "TestSalt"u8];
                });
                services.AddHeadlessSettings(setup => setup.UseEntityFramework<ContractTestDbContext>());
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
        var message = new SettingChangedMessage
        {
            SettingNames = ["App.Theme"],
            ProviderName = SettingValueProviderNames.Global,
            OriginHostName = "node-a",
        };

        // when
        await harness.Publisher.PublishAsync(message, cancellationToken: AbortToken);

        // then
        var published = await harness.WaitForPublished<SettingChangedMessage>(cancellationToken: AbortToken);
        var consumed = await harness.WaitForConsumed<SettingChangedMessage>(cancellationToken: AbortToken);
        published.MessageName.Should().Be("headless.settings.changed");
        consumed.MessageName.Should().Be("headless.settings.changed");
        harness
            .ServiceProvider.GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Should()
            .ContainSingle(metadata => metadata.ConsumerType == typeof(AppSettingChangedConsumer))
            .Which.MessageContractVersion.Should()
            .Be("1");
    }

    private sealed class ContractTestDbContext(DbContextOptions<ContractTestDbContext> options) : DbContext(options);
}

/// <summary>An application's consumer of the announcement, declared the way any host declares one.</summary>
[BusConsumer("tests.settings.changed-watcher", EveryInstance = true)]
public sealed class AppSettingChangedConsumer : IConsume<SettingChangedMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<SettingChangedMessage> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
