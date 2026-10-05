// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Testing;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestAssemblyMessagingModule = Headless.Permissions.Composition.Tests.Unit.MessagingModule;

namespace Tests.WireContracts;

/// <summary>
/// The permission announcements travel under names <c>AddHeadlessPermissions</c> declares, so services sharing a
/// broker agree on the topics whatever naming conventions each host configures for its own messages, and an
/// application consumer subscribes to them with no declaration of its own.
/// </summary>
public sealed class PermissionMessageContractTests : TestBase
{
    [Fact]
    public async Task should_publish_and_consume_the_grant_change_under_the_declared_name()
    {
        // given
        await using var harness = await _CreateHarnessAsync();
        var message = new PermissionGrantChangedMessage
        {
            PermissionNames = ["Orders.Create"],
            ProviderName = PermissionGrantProviderNames.Role,
            ProviderKey = "admin",
            OriginHostName = "node-a",
        };

        // when
        await harness.Publisher.PublishAsync(message, cancellationToken: AbortToken);

        // then
        var published = await harness.WaitForPublishedAsync<PermissionGrantChangedMessage>(
            cancellationToken: AbortToken
        );
        var consumed = await harness.WaitForConsumedAsync<PermissionGrantChangedMessage>(cancellationToken: AbortToken);
        published.MessageName.Should().Be("headless.permissions.grant-changed");
        consumed.MessageName.Should().Be("headless.permissions.grant-changed");
        _ContractVersionOf<AppPermissionGrantChangedConsumer>(harness).Should().Be("1");
    }

    [Fact]
    public async Task should_publish_and_consume_the_definitions_change_under_the_declared_name()
    {
        // given
        await using var harness = await _CreateHarnessAsync();
        var message = new DynamicPermissionDefinitionsChanged
        {
            UniqueId = "change-1",
            Permissions = ["Orders.Create"],
        };

        // when
        await harness.Publisher.PublishAsync(message, cancellationToken: AbortToken);

        // then
        var published = await harness.WaitForPublishedAsync<DynamicPermissionDefinitionsChanged>(
            cancellationToken: AbortToken
        );
        var consumed = await harness.WaitForConsumedAsync<DynamicPermissionDefinitionsChanged>(
            cancellationToken: AbortToken
        );
        published.MessageName.Should().Be("headless.permissions.definitions-changed");
        consumed.MessageName.Should().Be("headless.permissions.definitions-changed");
        _ContractVersionOf<AppPermissionDefinitionsChangedConsumer>(harness).Should().Be("1");
    }

    // Conventions that rename every message the host does not declare.
    private static Task<MessagingTestHarness> _CreateHarnessAsync()
    {
        return MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddHeadlessPermissions(setup => setup.UseEntityFramework<ContractTestDbContext>());
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
    }

    private static string _ContractVersionOf<TConsumer>(MessagingTestHarness harness)
    {
        return harness
            .ServiceProvider.GetRequiredService<IConsumerRegistry>()
            .GetAll()
            .Single(metadata => metadata.ConsumerType == typeof(TConsumer))
            .MessageContractVersion;
    }

    private sealed class ContractTestDbContext(DbContextOptions<ContractTestDbContext> options) : DbContext(options);
}

/// <summary>An application's consumer of the grant announcement, declared the way any host declares one.</summary>
[BusConsumer("tests.permissions.grant-changed-watcher", EveryInstance = true)]
public sealed class AppPermissionGrantChangedConsumer : IConsume<PermissionGrantChangedMessage>
{
    public ValueTask ConsumeAsync(
        ConsumeContext<PermissionGrantChangedMessage> context,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;
}

/// <summary>An application's consumer of the definitions announcement, declared as any host declares one.</summary>
[BusConsumer("tests.permissions.definitions-changed-watcher", EveryInstance = true)]
public sealed class AppPermissionDefinitionsChangedConsumer : IConsume<DynamicPermissionDefinitionsChanged>
{
    public ValueTask ConsumeAsync(
        ConsumeContext<DynamicPermissionDefinitionsChanged> context,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;
}
