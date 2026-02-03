// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Api.Identity.Authentication.ApiKey;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Identity.Authentication;

public sealed class ApiKeyAuthenticationHandlerTests : TestBase
{
    private readonly UserManager<TestUser> _userManager;
    private readonly SignInManager<TestUser> _signInManager;
    private readonly IApiKeyStore<TestUser, string> _apiKeyStore;
    private readonly IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> _optionsMonitor;

    public ApiKeyAuthenticationHandlerTests()
    {
        _userManager = Substitute.For<UserManager<TestUser>>(
            Substitute.For<IUserStore<TestUser>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        _signInManager = Substitute.For<SignInManager<TestUser>>(
            _userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<TestUser>>(),
            null,
            null,
            null,
            null
        );
        _apiKeyStore = Substitute.For<IApiKeyStore<TestUser, string>>();

        var options = new ApiKeyAuthenticationSchemeOptions();
        _optionsMonitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationSchemeOptions>>();
        _optionsMonitor.Get(Arg.Any<string>()).Returns(options);
        _optionsMonitor.CurrentValue.Returns(options);
    }

    [Fact]
    public async Task should_return_success_when_user_already_authenticated()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "testuser")], "TestScheme");
        context.User = new ClaimsPrincipal(identity);

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeTrue();
        result.Ticket.Should().NotBeNull();
        result.Ticket!.AuthenticationScheme.Should().Be("context.User");
    }

    [Fact]
    public async Task should_return_no_result_when_no_api_key_header_or_query()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_no_result_when_api_key_header_empty()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        context.Request.Headers["X-Api-Key"] = "";

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_no_result_when_api_key_query_empty()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        context.Request.QueryString = new QueryString("?api_key=");

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_no_result_when_api_key_user_not_found()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        context.Request.Headers["X-Api-Key"] = "invalid-api-key";
        _apiKeyStore.GetActiveApiKeyUserAsync("invalid-api-key").Returns(ValueTask.FromResult<TestUser?>(null));

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_fail_when_user_cannot_sign_in()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var user = new TestUser("user-1", "testuser", "test@example.com");
        context.Request.Headers["X-Api-Key"] = "valid-api-key";
        _apiKeyStore.GetActiveApiKeyUserAsync("valid-api-key").Returns(ValueTask.FromResult<TestUser?>(user));
        _signInManager.CanSignInAsync(user).Returns(Task.FromResult(false));

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Authentication failed.");
    }

    [Fact]
    public async Task should_return_fail_when_user_locked_out()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var user = new TestUser("user-1", "testuser", "test@example.com");
        context.Request.Headers["X-Api-Key"] = "valid-api-key";
        _apiKeyStore.GetActiveApiKeyUserAsync("valid-api-key").Returns(ValueTask.FromResult<TestUser?>(user));
        _signInManager.CanSignInAsync(user).Returns(Task.FromResult(true));
        _userManager.SupportsUserLockout.Returns(true);
        _userManager.IsLockedOutAsync(user).Returns(Task.FromResult(true));

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Authentication failed.");
    }

    [Fact]
    public async Task should_return_success_with_ticket_when_valid_api_key()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var user = new TestUser("user-1", "testuser", "test@example.com");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "testuser")]));

        context.Request.Headers["X-Api-Key"] = "valid-api-key";
        _apiKeyStore.GetActiveApiKeyUserAsync("valid-api-key").Returns(ValueTask.FromResult<TestUser?>(user));
        _signInManager.CanSignInAsync(user).Returns(Task.FromResult(true));
        _userManager.SupportsUserLockout.Returns(false);
        _signInManager.CreateUserPrincipalAsync(user).Returns(Task.FromResult(principal));

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeTrue();
        result.Ticket.Should().NotBeNull();
        result.Ticket!.AuthenticationScheme.Should().Be("ApiKey");
        result.Ticket.Principal.Should().BeSameAs(principal);
    }

    [Fact]
    public async Task should_use_header_over_query_when_both_present()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var headerUser = new TestUser("user-1", "headeruser", "header@example.com");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "headeruser")]));

        context.Request.Headers["X-Api-Key"] = "header-api-key";
        context.Request.QueryString = new QueryString("?api_key=query-api-key");

        _apiKeyStore.GetActiveApiKeyUserAsync("header-api-key").Returns(ValueTask.FromResult<TestUser?>(headerUser));
        _signInManager.CanSignInAsync(headerUser).Returns(Task.FromResult(true));
        _userManager.SupportsUserLockout.Returns(false);
        _signInManager.CreateUserPrincipalAsync(headerUser).Returns(Task.FromResult(principal));

        await handler.InitializeAsync(
            new AuthenticationScheme("ApiKey", null, typeof(ApiKeyAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeTrue();
        await _apiKeyStore.Received(1).GetActiveApiKeyUserAsync("header-api-key");
        await _apiKeyStore.DidNotReceive().GetActiveApiKeyUserAsync("query-api-key");
    }

    #region Helpers

    private ApiKeyAuthenticationHandler<TestUser, string> _CreateHandler()
    {
        return new ApiKeyAuthenticationHandler<TestUser, string>(
            _optionsMonitor,
            LoggerFactory,
            UrlEncoder.Default,
            _userManager,
            _signInManager,
            _apiKeyStore
        );
    }

    private static DefaultHttpContext _CreateContext()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        return context;
    }

    #endregion
}

public class TestUser : IdentityUser<string>
{
    public TestUser(string id, string userName, string email)
    {
        Id = id;
        UserName = userName;
        Email = email;
    }
}
