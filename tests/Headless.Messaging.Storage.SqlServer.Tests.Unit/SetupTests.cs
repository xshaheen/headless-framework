// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public async Task should_enable_transactional_outbox_by_default_on_entity_framework_path()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup => setup.UseEntityFramework<TestMessagingDbContext>());

        await using var provider = services.BuildServiceProvider();

        // then — a real commit coordinator (not the null fallback) is wired, and the interceptor auto-attach
        // configuration is registered for the consumer's DbContext.
        provider
            .GetRequiredService<ICurrentCommitCoordinator>()
            .GetType()
            .Name.Should()
            .NotBe("MessagingNullCommitCoordinator");
        provider
            .GetServices<IDbContextOptionsConfiguration<TestMessagingDbContext>>()
            .Should()
            .NotBeEmpty("the EF-context path auto-registers the commit-interceptor options configuration");
    }

    [Fact]
    public async Task should_not_enable_transactional_outbox_on_raw_path()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when — raw ADO storage, no DbContext
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSqlServer("Server=localhost;Database=test;TrustServerCertificate=True");
        });

        await using var provider = services.BuildServiceProvider();

        // then — the null fallback stays; no auto-registration.
        provider
            .GetRequiredService<ICurrentCommitCoordinator>()
            .GetType()
            .Name.Should()
            .Be("MessagingNullCommitCoordinator");
        provider.GetServices<IDbContextOptionsConfiguration<TestMessagingDbContext>>().Should().BeEmpty();
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

        await using var provider = services.BuildServiceProvider();

        // then — opt-out restores non-transactional immediate dispatch; no coordinator, no config.
        provider
            .GetRequiredService<ICurrentCommitCoordinator>()
            .GetType()
            .Name.Should()
            .Be("MessagingNullCommitCoordinator");
        provider.GetServices<IDbContextOptionsConfiguration<TestMessagingDbContext>>().Should().BeEmpty();
    }

    private sealed class TestMessagingDbContext(DbContextOptions<TestMessagingDbContext> options) : DbContext(options);
}
