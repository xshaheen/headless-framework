// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity.TokenProviders;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Identity.TokenProviders;

public sealed class IdentityBuilderExtensionsRegistrationTests
{
    [Fact]
    public void should_register_configured_password_reset_code_as_identity_default()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        var identity = services.AddIdentityCore<RegistrationTokenUser>();

        // when
        identity.AddPasswordResetCodeProvider<RegistrationTokenUser>(options =>
        {
            options.Name = "ShortResetCode";
            options.Timestep = TimeSpan.FromSeconds(45);
            options.Variance = 1;
            options.HashMode = TotpHashMode.Sha256;
        });
        using var provider = services.BuildServiceProvider();

        // then
        var identityOptions = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var providerOptions = provider.GetRequiredService<IOptions<PasswordResetCodeProviderOptions>>().Value;
        identityOptions.Tokens.PasswordResetTokenProvider.Should().Be("ShortResetCode");
        identityOptions
            .Tokens.ProviderMap["ShortResetCode"]
            .ProviderType.Should()
            .Be<PasswordResetCodeProvider<RegistrationTokenUser>>();
        providerOptions.Timestep.Should().Be(TimeSpan.FromSeconds(45));
        providerOptions.Variance.Should().Be(1);
        providerOptions.HashMode.Should().Be(TotpHashMode.Sha256);
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(TotpRfc6238Generator));
    }

    [Fact]
    public void should_register_configured_email_confirmation_token_as_identity_default()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        var identity = services.AddIdentityCore<RegistrationTokenUser>();

        // when
        identity.AddEmailConfirmationTokenProvider<RegistrationTokenUser>(options =>
        {
            options.Name = "EmailLink";
            options.TokenLifespan = TimeSpan.FromMinutes(20);
        });
        using var provider = services.BuildServiceProvider();

        // then
        var identityOptions = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var providerOptions = provider.GetRequiredService<IOptions<EmailConfirmationTokenProviderOptions>>().Value;
        identityOptions.Tokens.EmailConfirmationTokenProvider.Should().Be("EmailLink");
        identityOptions
            .Tokens.ProviderMap["EmailLink"]
            .ProviderType.Should()
            .Be<EmailConfirmationTokenProvider<RegistrationTokenUser>>();
        providerOptions.TokenLifespan.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void should_keep_totp_generator_singleton_when_both_code_providers_are_registered()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        var identity = services.AddIdentityCore<RegistrationTokenUser>();

        // when
        identity.AddPasswordResetCodeProvider<RegistrationTokenUser>(null);
        identity.AddEmailConfirmationCodeProvider<RegistrationTokenUser>(null);

        // then
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(TotpRfc6238Generator));
    }

    private sealed class RegistrationTokenUser;
}
