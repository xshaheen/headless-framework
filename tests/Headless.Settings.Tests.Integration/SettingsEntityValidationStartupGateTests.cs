// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
