// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Abstractions;
using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class CoreSettingsSetupTests
{
    [Fact]
    public void add_headless_settings_should_require_string_encryption_service()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when - the management core (auto-registered by AddHeadlessSettings) requires encryption
        var action = () =>
            builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(IStringEncryptionService)}*");
    }

    [Fact]
    public void add_headless_settings_should_not_override_existing_string_encryption_registration()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "ExplicitPassPhrase123";
            options.DefaultSalt = "ExplicitSalt"u8.ToArray();
        });

        // when
        builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<OptionsTestDbContext>());

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var encryptionOptions = serviceProvider.GetRequiredService<IOptions<StringEncryptionOptions>>().Value;

        // then
        encryptionOptions.DefaultPassPhrase.Should().Be("ExplicitPassPhrase123");
        encryptionOptions.DefaultSalt.Should().BeEquivalentTo("ExplicitSalt"u8.ToArray());
    }

    private sealed class OptionsTestDbContext(DbContextOptions<OptionsTestDbContext> options) : DbContext(options);
}
