// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Security;
using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

public sealed class SettingsEntityValidationStartupGateTests(SettingsTestFixture fixture) : SettingsTestBase(fixture)
{
    [Fact]
    public async Task should_fail_startup_when_shared_dbcontext_does_not_include_settings_entities()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        // AddHeadlessSettings auto-registers the management core, which requires
        // IStringEncryptionService (its _AddCore guard) and TimeProvider (its initialization hosted
        // service) — register both so startup reaches the entity-validation gate rather than throwing
        // a missing-dependency error first.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "TestPassPhrase123456";
            options.DefaultSalt = "TestSalt"u8.ToArray();
        });
        builder.Services.AddDbContextFactory<MissingSettingsEntityDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );
        builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<MissingSettingsEntityDbContext>());
        using var host = builder.Build();

        // when
        var action = async () => await host.StartAsync(AbortToken);

        // then
        await action
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*SettingValueRecord*modelBuilder.AddHeadlessSettings*");
    }

    private sealed class MissingSettingsEntityDbContext(DbContextOptions<MissingSettingsEntityDbContext> options)
        : DbContext(options);
}
