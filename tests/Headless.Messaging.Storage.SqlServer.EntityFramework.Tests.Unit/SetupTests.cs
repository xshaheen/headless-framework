// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    public async Task should_enable_transactional_inbox_by_default_on_entity_framework_path()
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
            scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().Should().NotBeNull();
        }

        services
            .Should()
            .ContainSingle(descriptor =>
                descriptor.ServiceType.Name == "IInboxTransactionRunner"
                && descriptor.Lifetime == ServiceLifetime.Scoped
            );

        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(capability => string.Equals(capability.Provider, "SqlServer", StringComparison.Ordinal))
            .InboxGuarantee.Should()
            .Be(InboxGuarantee.Transactional);
    }

    [Fact]
    public async Task should_opt_out_of_transactional_inbox_when_requested()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when — EF path but explicitly opted out via the per-storage flag
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseEntityFramework<TestMessagingDbContext>(o => o.EnableTransactionalInbox = false);
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then — opt-out restores non-transactional immediate dispatch; no inbox transaction runner.
        services.Should().NotContain(descriptor => descriptor.ServiceType.Name == "IInboxTransactionRunner");
        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(capability => string.Equals(capability.Provider, "SqlServer", StringComparison.Ordinal))
            .InboxGuarantee.Should()
            .Be(InboxGuarantee.Durable);
    }

    [Fact]
    public async Task should_copy_the_context_connection_string_with_its_password()
    {
        // given
        const string connectionString = "Server=localhost;Database=entity;User ID=sa;Password=secret";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestMessagingDbContext>(builder => builder.UseSqlServer(connectionString));
        services.AddHeadlessMessaging(setup => setup.UseEntityFramework<TestMessagingDbContext>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // when
        var options = provider.GetRequiredService<IOptions<SqlServerOptions>>().Value;

        // then
        new SqlConnectionStringBuilder(options.ConnectionString)
            .Password.Should()
            .Be("secret");
    }

    [Fact]
    public async Task should_fail_at_startup_when_the_context_connection_authenticates_with_an_access_token()
    {
        // given: a token a connection string cannot carry, so a copied string would fail at the first login
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestMessagingDbContext>(builder =>
            builder.UseSqlServer(new SqlConnection("Server=localhost;Database=entity") { AccessToken = "token" })
        );
        services.AddHeadlessMessaging(setup => setup.UseEntityFramework<TestMessagingDbContext>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // when
        var act = () => provider.GetRequiredService<IOptions<SqlServerOptions>>().Value;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*access token*");
    }

    private sealed class TestMessagingDbContext(DbContextOptions<TestMessagingDbContext> options) : DbContext(options);
}
