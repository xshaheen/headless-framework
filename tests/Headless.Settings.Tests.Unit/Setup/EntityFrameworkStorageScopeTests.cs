// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Domain;
using Headless.Settings;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Setup;

/// <summary>
/// Regression guard for the captive-dependency fix: the EF value repository is a singleton but must not
/// capture the scoped <see cref="ILocalEventBus"/>. It resolves the bus from a short-lived scope per publish,
/// so it stays resolvable from the root scope under <c>ValidateScopes</c> (ASP.NET Core's Development default).
/// </summary>
public sealed class EntityFrameworkStorageScopeTests
{
    [Fact]
    public void ef_value_repository_should_resolve_as_singleton_when_scoped_event_bus_is_registered()
    {
        // given - a host that wires the scoped ILocalEventBus alongside EF settings storage, with scope validation on
        var services = new ServiceCollection();
        services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "RegressionPassPhrase123";
            options.DefaultSalt = "RegressionSalt"u8.ToArray();
        });
        services.AddDbContextFactory<ScopeTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        services.AddHeadlessLocalEventBus();
        services.AddHeadlessSettings(setup => setup.UseEntityFramework<ScopeTestDbContext>());

        using var provider = services.BuildServiceProvider(validateScopes: true);

        // when - the singleton repo is resolved from the root scope
        var act = () => provider.GetRequiredService<ISettingValueRecordRepository>();

        // then - no captive-dependency violation and the repo stays a singleton
        var repo = act.Should().NotThrow().Which;
        provider.GetRequiredService<ISettingValueRecordRepository>().Should().BeSameAs(repo);
    }

    private sealed class ScopeTestDbContext(DbContextOptions<ScopeTestDbContext> options) : DbContext(options);
}
