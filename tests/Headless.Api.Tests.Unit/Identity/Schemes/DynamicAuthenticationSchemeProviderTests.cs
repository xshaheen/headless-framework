// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity.Authentication.ApiKey;
using Headless.Api.Identity.Authentication.Basic;
using Headless.Api.Identity.Schemes;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
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
    [InlineData(nameof(DynamicAuthenticationSchemeProvider.GetDefaultSignInSchemeAsync))]
    [InlineData(nameof(DynamicAuthenticationSchemeProvider.GetDefaultSignOutSchemeAsync))]
    public async Task should_work_for_all_get_default_methods(string methodName)
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
            nameof(DynamicAuthenticationSchemeProvider.GetDefaultSignInSchemeAsync) =>
                await provider.GetDefaultSignInSchemeAsync(),
            nameof(DynamicAuthenticationSchemeProvider.GetDefaultSignOutSchemeAsync) =>
                await provider.GetDefaultSignOutSchemeAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(methodName)),
        };

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(AuthenticationConstants.Schemas.ApiKey);
    }

    #region Helpers

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
