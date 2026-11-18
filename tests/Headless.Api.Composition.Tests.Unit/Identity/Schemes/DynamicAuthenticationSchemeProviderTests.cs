// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity;
using Headless.Http;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Tests.Identity.Schemes;

public sealed class DynamicAuthenticationSchemeProviderTests : TestBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<AuthenticationOptions> _authOptions;
    private readonly IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> _apiKeyOptions;

    public DynamicAuthenticationSchemeProviderTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

        var authOptions = new AuthenticationOptions();
        authOptions.AddScheme<TestAuthHandler>(AuthenticationConstants.Schemas.ApiKey, null);
        authOptions.AddScheme<TestAuthHandler>(AuthenticationConstants.Schemas.Basic, null);
        authOptions.AddScheme<TestAuthHandler>(AuthenticationConstants.Schemas.Bearer, null);
        authOptions.DefaultAuthenticateScheme = AuthenticationConstants.Schemas.Bearer;
        authOptions.DefaultChallengeScheme = AuthenticationConstants.Schemas.Bearer;
        authOptions.DefaultForbidScheme = AuthenticationConstants.Schemas.Bearer;
        authOptions.DefaultSignInScheme = AuthenticationConstants.Schemas.Bearer;
        authOptions.DefaultSignOutScheme = AuthenticationConstants.Schemas.Bearer;

        _authOptions = Options.Create(authOptions);
        _apiKeyOptions = _CreateApiKeyOptionsMonitor(new ApiKeyAuthenticationSchemeOptions());
    }

    [Fact]
    public void should_use_canonical_basic_and_api_key_scheme_names_by_default()
    {
        BasicAuthenticationOptions.DefaultScheme.Should().Be(AuthenticationConstants.Schemas.Basic);
        ApiKeyAuthenticationSchemeOptions.DefaultScheme.Should().Be(AuthenticationConstants.Schemas.ApiKey);
    }

    [Fact]
    public async Task should_fallback_to_base_default_when_no_http_context()
    {
        // given
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        var provider = _CreateProvider();

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then - falls back to base default scheme (Bearer)
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.Bearer);
    }

    [Fact]
    public async Task should_return_api_key_scheme_when_api_key_header_present()
    {
        // given
        var context = _CreateContext();
        context.Request.Headers[HttpHeaderNames.ApiKey] = "test-api-key";
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.ApiKey);
    }

    [Fact]
    public async Task should_fallback_to_base_when_api_key_query_present_but_query_not_opted_in()
    {
        // given
        var context = _CreateContext();
        context.Request.QueryString = new QueryString("?api_key=test-api-key");
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.Bearer);
    }

    [Fact]
    public async Task should_return_api_key_scheme_when_api_key_query_present_and_query_opted_in()
    {
        // given
        var context = _CreateContext();
        context.Request.QueryString = new QueryString("?api_key=test-api-key");
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider(new ApiKeyAuthenticationSchemeOptions { AllowApiKeyInQueryString = true });

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.ApiKey);
    }

    [Fact]
    public async Task should_return_basic_scheme_when_basic_auth_header()
    {
        // given
        var context = _CreateContext();
        context.Request.Headers[HttpHeaderNames.Authorization] = "Basic dXNlcm5hbWU6cGFzc3dvcmQ=";
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.Basic);
    }

    [Fact]
    public async Task should_return_bearer_scheme_when_bearer_auth_header()
    {
        // given
        var context = _CreateContext();
        context.Request.Headers[HttpHeaderNames.Authorization] = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.Bearer);
    }

    [Fact]
    public async Task should_return_bearer_scheme_when_non_basic_auth_header()
    {
        // given
        var context = _CreateContext();
        context.Request.Headers[HttpHeaderNames.Authorization] = "Digest username=test";
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.Bearer);
    }

    [Fact]
    public async Task should_fallback_to_base_when_no_scheme_detected()
    {
        // given
        var context = _CreateContext();
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.Bearer);
    }

    [Fact]
    public async Task should_check_api_key_before_authorization_header()
    {
        // given
        var context = _CreateContext();
        context.Request.Headers[HttpHeaderNames.ApiKey] = "test-api-key";
        context.Request.Headers[HttpHeaderNames.Authorization] = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.ApiKey);
    }

    [Theory]
    [InlineData(nameof(DynamicAuthenticationSchemeProvider.GetDefaultAuthenticateSchemeAsync))]
    [InlineData(nameof(DynamicAuthenticationSchemeProvider.GetDefaultChallengeSchemeAsync))]
    [InlineData(nameof(DynamicAuthenticationSchemeProvider.GetDefaultForbidSchemeAsync))]
    public async Task should_select_the_request_scheme_for_authenticate_challenge_and_forbid(string methodName)
    {
        // given
        var context = _CreateContext();
        context.Request.Headers[HttpHeaderNames.ApiKey] = "test-api-key";
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var result = methodName switch
        {
            nameof(DynamicAuthenticationSchemeProvider.GetDefaultAuthenticateSchemeAsync) =>
                await provider.GetDefaultAuthenticateSchemeAsync(),
            nameof(DynamicAuthenticationSchemeProvider.GetDefaultChallengeSchemeAsync) =>
                await provider.GetDefaultChallengeSchemeAsync(),
            nameof(DynamicAuthenticationSchemeProvider.GetDefaultForbidSchemeAsync) =>
                await provider.GetDefaultForbidSchemeAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(methodName)),
        };

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.ApiKey);
    }

    [Fact]
    public async Task should_keep_the_configured_sign_in_and_sign_out_schemes_when_request_carries_header_credentials()
    {
        // given - none of the header schemes can sign in or out, so selecting one made SignOutAsync() throw
        var context = _CreateContext();
        context.Request.Headers[HttpHeaderNames.ApiKey] = "test-api-key";
        _httpContextAccessor.HttpContext.Returns(context);
        var provider = _CreateProvider();

        // when
        var signIn = await provider.GetDefaultSignInSchemeAsync();
        var signOut = await provider.GetDefaultSignOutSchemeAsync();

        // then
        signIn!.Name.Should().Be(AuthenticationConstants.Schemas.Bearer);
        signOut!.Name.Should().Be(AuthenticationConstants.Schemas.Bearer);
    }

    [Fact]
    public async Task should_select_a_custom_named_api_key_scheme()
    {
        // given
        var provider = _CreateCustomProvider(
            options => options.AddScheme(_PartnerKey, b => b.HandlerType = _ApiKeyHandler),
            (_PartnerKey, new ApiKeyAuthenticationSchemeOptions())
        );
        _httpContextAccessor.HttpContext.Returns(_CreateContext(HttpHeaderNames.ApiKey, "test-api-key"));

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result!.Name.Should().Be(_PartnerKey);
    }

    [Fact]
    public async Task should_select_the_api_key_scheme_whose_own_header_the_request_carries()
    {
        // given
        var provider = _CreateCustomProvider(
            options =>
            {
                options.AddScheme(AuthenticationConstants.Schemas.ApiKey, b => b.HandlerType = _ApiKeyHandler);
                options.AddScheme(_PartnerKey, b => b.HandlerType = _ApiKeyHandler);
            },
            (AuthenticationConstants.Schemas.ApiKey, new ApiKeyAuthenticationSchemeOptions()),
            (_PartnerKey, new ApiKeyAuthenticationSchemeOptions { ApiKeyHeaderName = "X-Partner-Key" })
        );
        _httpContextAccessor.HttpContext.Returns(_CreateContext("X-Partner-Key", "partner-key"));

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result!.Name.Should().Be(_PartnerKey);
    }

    [Fact]
    public async Task should_prefer_the_canonical_api_key_scheme_when_several_match()
    {
        // given
        var provider = _CreateCustomProvider(
            options =>
            {
                options.AddScheme(_PartnerKey, b => b.HandlerType = _ApiKeyHandler);
                options.AddScheme(AuthenticationConstants.Schemas.ApiKey, b => b.HandlerType = _ApiKeyHandler);
            },
            (AuthenticationConstants.Schemas.ApiKey, new ApiKeyAuthenticationSchemeOptions()),
            (_PartnerKey, new ApiKeyAuthenticationSchemeOptions())
        );
        _httpContextAccessor.HttpContext.Returns(_CreateContext(HttpHeaderNames.ApiKey, "test-api-key"));

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result!.Name.Should().Be(AuthenticationConstants.Schemas.ApiKey);
    }

    [Fact]
    public async Task should_fall_back_to_the_configured_default_when_several_custom_api_key_schemes_match()
    {
        // given
        var provider = _CreateCustomProvider(
            options =>
            {
                options.AddScheme(_PartnerKey, b => b.HandlerType = _ApiKeyHandler);
                options.AddScheme("VendorKey", b => b.HandlerType = _ApiKeyHandler);
            },
            (_PartnerKey, new ApiKeyAuthenticationSchemeOptions()),
            ("VendorKey", new ApiKeyAuthenticationSchemeOptions())
        );
        _httpContextAccessor.HttpContext.Returns(_CreateContext(HttpHeaderNames.ApiKey, "test-api-key"));

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result!.Name.Should().Be(_Cookies);
    }

    [Fact]
    public async Task should_select_a_custom_named_basic_scheme()
    {
        // given
        var provider = _CreateCustomProvider(options =>
            options.AddScheme("partner-basic", b => b.HandlerType = _BasicHandler)
        );
        _httpContextAccessor.HttpContext.Returns(
            _CreateContext(HttpHeaderNames.Authorization, "Basic dXNlcm5hbWU6cGFzc3dvcmQ=")
        );

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result!.Name.Should().Be("partner-basic");
    }

    [Fact]
    public async Task should_select_a_custom_named_jwt_bearer_scheme()
    {
        // given
        var provider = _CreateCustomProvider(options =>
            options.AddScheme("PartnerJwt", b => b.HandlerType = typeof(JwtBearerHandler))
        );
        _httpContextAccessor.HttpContext.Returns(_CreateContext(HttpHeaderNames.Authorization, "Bearer token"));

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result!.Name.Should().Be("PartnerJwt");
    }

    [Fact]
    public async Task should_prefer_the_canonical_bearer_scheme_over_another_jwt_bearer_scheme()
    {
        // given
        var provider = _CreateCustomProvider(options =>
        {
            options.AddScheme("PartnerJwt", b => b.HandlerType = typeof(JwtBearerHandler));
            options.AddScheme(AuthenticationConstants.Schemas.Bearer, b => b.HandlerType = typeof(JwtBearerHandler));
        });
        _httpContextAccessor.HttpContext.Returns(_CreateContext(HttpHeaderNames.Authorization, "Bearer token"));

        // when
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result!.Name.Should().Be(AuthenticationConstants.Schemas.Bearer);
    }

    [Fact]
    public async Task should_select_a_scheme_added_after_the_first_request()
    {
        // given
        var provider = _CreateCustomProvider(_ => { }, (_PartnerKey, new ApiKeyAuthenticationSchemeOptions()));
        _httpContextAccessor.HttpContext.Returns(_CreateContext(HttpHeaderNames.ApiKey, "test-api-key"));
        (await provider.GetDefaultAuthenticateSchemeAsync())!.Name.Should().Be(_Cookies);

        // when
        provider.AddScheme(new AuthenticationScheme(_PartnerKey, displayName: null, _ApiKeyHandler));
        var result = await provider.GetDefaultAuthenticateSchemeAsync();

        // then
        result!.Name.Should().Be(_PartnerKey);
    }

    #region Helpers

    private const string _PartnerKey = "PartnerKey";
    private const string _Cookies = "Cookies";

    // The provider classifies schemes by handler type and never constructs the handler.
    private static readonly Type _ApiKeyHandler = typeof(ApiKeyAuthenticationHandler<IdentityUser<Guid>, Guid>);
    private static readonly Type _BasicHandler = typeof(BasicAuthenticationHandler<IdentityUser<Guid>, Guid>);

    private DynamicAuthenticationSchemeProvider _CreateCustomProvider(
        Action<AuthenticationOptions> addSchemes,
        params (string Scheme, ApiKeyAuthenticationSchemeOptions Options)[] apiKeyOptions
    )
    {
        var options = new AuthenticationOptions { DefaultScheme = _Cookies };
        options.AddScheme<TestAuthHandler>(_Cookies, null);
        addSchemes(options);

        var monitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationSchemeOptions>>();
        monitor.Get(Arg.Any<string>()).Returns(new ApiKeyAuthenticationSchemeOptions());

        foreach (var (scheme, schemeOptions) in apiKeyOptions)
        {
            monitor.Get(scheme).Returns(schemeOptions);
        }

        return new DynamicAuthenticationSchemeProvider(_httpContextAccessor, Options.Create(options), monitor);
    }

    private static DefaultHttpContext _CreateContext(string header, string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[header] = value;

        return context;
    }

    private DynamicAuthenticationSchemeProvider _CreateProvider()
    {
        return new DynamicAuthenticationSchemeProvider(_httpContextAccessor, _authOptions, _apiKeyOptions);
    }

    private DynamicAuthenticationSchemeProvider _CreateProvider(ApiKeyAuthenticationSchemeOptions apiKeyOptions)
    {
        return new DynamicAuthenticationSchemeProvider(
            _httpContextAccessor,
            _authOptions,
            _CreateApiKeyOptionsMonitor(apiKeyOptions)
        );
    }

    private static IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> _CreateApiKeyOptionsMonitor(
        ApiKeyAuthenticationSchemeOptions options
    )
    {
        var monitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationSchemeOptions>>();
        monitor.CurrentValue.Returns(options);
        monitor.Get(Arg.Any<string>()).Returns(options);

        return monitor;
    }

    private static DefaultHttpContext _CreateContext()
    {
        return new DefaultHttpContext();
    }

    #endregion
}

internal sealed class TestAuthHandler : IAuthenticationHandler
{
    public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context)
    {
        return Task.CompletedTask;
    }

    public Task<AuthenticateResult> AuthenticateAsync()
    {
        return Task.FromResult(AuthenticateResult.NoResult());
    }

    public Task ChallengeAsync(AuthenticationProperties? properties)
    {
        return Task.CompletedTask;
    }

    public Task ForbidAsync(AuthenticationProperties? properties)
    {
        return Task.CompletedTask;
    }
}
