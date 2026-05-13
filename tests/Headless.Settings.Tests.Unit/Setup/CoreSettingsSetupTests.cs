// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Abstractions;
using Headless.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class CoreSettingsSetupTests
{
    [Fact]
    public void add_settings_management_core_should_require_string_encryption_service()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        var action = () => builder.Services.AddSettingsManagementCore();

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(IStringEncryptionService)}*");
    }

    [Fact]
    public void add_settings_management_core_should_not_override_existing_string_encryption_registration()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "ExplicitPassPhrase123";
            options.InitVectorBytes = "ExplicitInitVect"u8.ToArray();
            options.DefaultSalt = "ExplicitSalt"u8.ToArray();
        });

        // when
        builder.Services.AddSettingsManagementCore();

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var encryptionOptions = serviceProvider.GetRequiredService<IOptions<StringEncryptionOptions>>().Value;

        // then
        encryptionOptions.DefaultPassPhrase.Should().Be("ExplicitPassPhrase123");
        encryptionOptions.InitVectorBytes.Should().BeEquivalentTo("ExplicitInitVect"u8.ToArray());
        encryptionOptions.DefaultSalt.Should().BeEquivalentTo("ExplicitSalt"u8.ToArray());
    }
}
