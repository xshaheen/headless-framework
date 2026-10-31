// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Setup;

public sealed class CoreSettingsSetupTests
{
    [Fact]
    public void should_register_settings_without_encryption_when_add_headless_settings()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when - encryption is resolved only when an encrypted setting is read or written
        builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ISettingEncryptionService>().Should().BeOfType<SettingEncryptionService>();
    }

    private sealed class OptionsTestDbContext(DbContextOptions<OptionsTestDbContext> options) : DbContext(options);
}
