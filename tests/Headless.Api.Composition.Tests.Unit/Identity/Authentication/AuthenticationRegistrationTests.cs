// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Identity.Authentication;

public sealed class AuthenticationRegistrationTests
{
    [Fact]
    public async Task should_register_custom_api_key_scheme_options_and_transient_store()
    {
        // given
        IServiceCollection services = new ServiceCollection();

        // when
        services
            .AddAuthentication()
            .AddApiKey<RegistrationUser, string, RegistrationApiKeyStore>(
                "PartnerKey",
                "Partner API key",
                options =>
                {
                    options.ApiKeyHeaderName = "X-Partner-Key";
                    options.AllowApiKeyInQueryString = true;
                    options.ApiKeyParamName = "partner_key";
                }
            );
        await using var provider = services.BuildServiceProvider();

        // then
        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync("PartnerKey");
        var options = provider
            .GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationSchemeOptions>>()
            .Get("PartnerKey");
        scheme.Should().NotBeNull();
        scheme!.DisplayName.Should().Be("Partner API key");
        scheme.HandlerType.Should().Be<ApiKeyAuthenticationHandler<RegistrationUser, string>>();
        options.ApiKeyHeaderName.Should().Be("X-Partner-Key");
        options.AllowApiKeyInQueryString.Should().BeTrue();
        options.ApiKeyParamName.Should().Be("partner_key");
        provider
            .GetRequiredService<IApiKeyStore<RegistrationUser, string>>()
            .Should()
            .BeOfType<RegistrationApiKeyStore>();
        provider
            .GetRequiredService<IApiKeyStore<RegistrationUser, string>>()
            .Should()
            .NotBeSameAs(provider.GetRequiredService<IApiKeyStore<RegistrationUser, string>>());
    }

    [Fact]
    public async Task should_register_basic_scheme_with_defaults_and_custom_options()
    {
        // given
        IServiceCollection services = new ServiceCollection();

        // when
        services
            .AddAuthentication()
            .AddBasicSchema<RegistrationUser, string>(configureOptions: options =>
                options.ClaimsIssuer = "InternalBasic"
            );
        await using var provider = services.BuildServiceProvider();

        // then
        var scheme = await provider
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(BasicAuthenticationOptions.DefaultScheme);
        var options = provider
            .GetRequiredService<IOptionsMonitor<BasicAuthenticationOptions>>()
            .Get(BasicAuthenticationOptions.DefaultScheme);
        scheme.Should().NotBeNull();
        scheme!.DisplayName.Should().Be(BasicAuthenticationOptions.DefaultScheme);
        scheme.HandlerType.Should().Be<BasicAuthenticationHandler<RegistrationUser, string>>();
        options.ClaimsIssuer.Should().Be("InternalBasic");
    }

    [Fact]
    public async Task should_replace_the_stock_scheme_provider_with_the_dynamic_one()
    {
        // given
        IServiceCollection services = new ServiceCollection();

        // when
        services.AddAuthentication().AddBasicSchema<RegistrationUser, string>();
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .Should()
            .BeOfType<DynamicAuthenticationSchemeProvider>();
    }

    [Fact]
    public async Task should_keep_a_scheme_provider_the_application_registered_before_the_header_schemes()
    {
        // given - the provider used to be replaced unconditionally, so the outcome depended on call order
        IServiceCollection services = new ServiceCollection();
        services.AddAuthentication();
        services.AddSingleton<IAuthenticationSchemeProvider, CustomSchemeProvider>();

        // when
        services
            .AddAuthentication()
            .AddBasicSchema<RegistrationUser, string>()
            .AddApiKey<RegistrationUser, string, RegistrationApiKeyStore>();
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IAuthenticationSchemeProvider>().Should().BeOfType<CustomSchemeProvider>();
    }

    private sealed class CustomSchemeProvider(IOptions<AuthenticationOptions> options)
        : AuthenticationSchemeProvider(options);

    private sealed class RegistrationUser : IdentityUser<string>;

    private sealed class RegistrationApiKeyStore : IApiKeyStore<RegistrationUser, string>
    {
        public ValueTask<RegistrationUser?> GetActiveApiKeyUserAsync(
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult<RegistrationUser?>(null);
        }
    }
}
