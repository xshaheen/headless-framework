// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_preserve_sqlserver_entity_framework_adapter_name_and_root()
    {
        typeof(SetupSqlServerEntityFrameworkMessaging).Name.Should().Be("SetupSqlServerEntityFrameworkMessaging");
        typeof(SetupSqlServerEntityFrameworkMessaging)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Should()
            .Contain(method => method.Name == "UseEntityFramework" && method.IsGenericMethodDefinition);
    }

    [Fact]
    public async Task should_enable_transactional_outbox_by_default_on_entity_framework_path()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup => setup.UseEntityFramework<TestMessagingDbContext>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then — the scoped unit-of-work manager is wired and the inbox transaction runner is registered.
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Should().NotBeNull();
        }

        services
            .Should()
            .ContainSingle(descriptor =>
                descriptor.ServiceType.Name == "IInboxTransactionRunner"
                && descriptor.Lifetime == ServiceLifetime.Scoped
            );
        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(capability => capability.Provider == "SqlServer")
            .InboxCapability.Should()
            .Be(MessagingInboxCapabilityTier.Transactional);
    }

    [Fact]
    public async Task should_opt_out_of_transactional_outbox_when_requested()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when — EF path but explicitly opted out via the per-storage flag
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseEntityFramework<TestMessagingDbContext>(o => o.EnableTransactionalOutbox = false);
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then — opt-out restores non-transactional immediate dispatch; no inbox transaction runner.
        services.Should().NotContain(descriptor => descriptor.ServiceType.Name == "IInboxTransactionRunner");
        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(capability => capability.Provider == "SqlServer")
            .InboxCapability.Should()
            .Be(MessagingInboxCapabilityTier.DurableDedupeOnly);
    }

    private sealed class TestMessagingDbContext(DbContextOptions<TestMessagingDbContext> options) : DbContext(options);
}
