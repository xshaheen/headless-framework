// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Api.Identity.Authentication.Basic;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Identity.Authentication;

public sealed class BasicAuthenticationHandlerTests : TestBase
{
    private readonly UserManager<TestUser> _userManager;
    private readonly SignInManager<TestUser> _signInManager;
    private readonly IOptionsMonitor<BasicAuthenticationOptions> _optionsMonitor;
    private readonly ILogger<BasicAuthenticationHandler<TestUser, string>> _logger;

    public BasicAuthenticationHandlerTests()
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

        var options = new BasicAuthenticationOptions { Scheme = "Basic Authentication" };
        _optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        _optionsMonitor.Get(Arg.Any<string>()).Returns(options);
        _optionsMonitor.CurrentValue.Returns(options);

        _logger = Substitute.For<ILogger<BasicAuthenticationHandler<TestUser, string>>>();
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
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
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
    public async Task should_return_no_result_when_no_authorization_header()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_no_result_when_not_basic_scheme()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        context.Request.Headers.Authorization = "Bearer some-token";

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_fail_when_invalid_base64_encoding()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        context.Request.Headers.Authorization = "Basic not-valid-base64!!!";

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Invalid Authorization header value.");
    }

    [Fact]
    public async Task should_return_fail_when_no_colon_separator()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var credentials = "usernamenopassword".ToBase64();
        context.Request.Headers.Authorization = $"Basic {credentials}";

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Invalid Authorization header value.");
    }

    [Fact]
    public async Task should_return_fail_when_user_not_found()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var credentials = "testuser:password123".ToBase64();
        context.Request.Headers.Authorization = $"Basic {credentials}";
        _userManager.FindByNameAsync("testuser").Returns(Task.FromResult<TestUser?>(null));

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Invalid user name or password.");
    }

    [Fact]
    public async Task should_return_fail_when_user_cannot_sign_in()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var user = new TestUser("user-1", "testuser", "test@example.com");
        var credentials = "testuser:password123".ToBase64();
        context.Request.Headers.Authorization = $"Basic {credentials}";
        _userManager.FindByNameAsync("testuser").Returns(Task.FromResult<TestUser?>(user));
        _signInManager.CanSignInAsync(user).Returns(Task.FromResult(false));

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Invalid user name or password.");
    }

    [Fact]
    public async Task should_return_fail_when_user_locked_out()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var user = new TestUser("user-1", "testuser", "test@example.com");
        var credentials = "testuser:password123".ToBase64();
        context.Request.Headers.Authorization = $"Basic {credentials}";
        _userManager.FindByNameAsync("testuser").Returns(Task.FromResult<TestUser?>(user));
        _signInManager.CanSignInAsync(user).Returns(Task.FromResult(true));
        _userManager.SupportsUserLockout.Returns(true);
        _userManager.IsLockedOutAsync(user).Returns(Task.FromResult(true));

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Invalid user name or password.");
    }

    [Fact]
    public async Task should_return_fail_when_wrong_password()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var user = new TestUser("user-1", "testuser", "test@example.com");
        var credentials = "testuser:wrongpassword".ToBase64();
        context.Request.Headers.Authorization = $"Basic {credentials}";
        _userManager.FindByNameAsync("testuser").Returns(Task.FromResult<TestUser?>(user));
        _signInManager.CanSignInAsync(user).Returns(Task.FromResult(true));
        _userManager.SupportsUserLockout.Returns(false);
        _userManager.CheckPasswordAsync(user, "wrongpassword").Returns(Task.FromResult(false));

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Invalid user name or password.");
    }

    [Fact]
    public async Task should_return_success_with_ticket_when_valid_credentials()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var user = new TestUser("user-1", "testuser", "test@example.com");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "testuser")]));
        var credentials = "testuser:correctpassword".ToBase64();

        context.Request.Headers.Authorization = $"Basic {credentials}";
        _userManager.FindByNameAsync("testuser").Returns(Task.FromResult<TestUser?>(user));
        _signInManager.CanSignInAsync(user).Returns(Task.FromResult(true));
        _userManager.SupportsUserLockout.Returns(false);
        _userManager.CheckPasswordAsync(user, "correctpassword").Returns(Task.FromResult(true));
        _signInManager.CreateUserPrincipalAsync(user).Returns(Task.FromResult(principal));

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then
        result.Succeeded.Should().BeTrue();
        result.Ticket.Should().NotBeNull();
        result.Ticket!.AuthenticationScheme.Should().Be("Basic Authentication");
        result.Ticket.Principal.Should().BeSameAs(principal);
    }

    [Fact]
    public async Task should_use_generic_error_message_to_prevent_enumeration()
    {
        // given
        var handler = _CreateHandler();
        var context = _CreateContext();
        var credentials = "nonexistentuser:anypassword".ToBase64();
        context.Request.Headers.Authorization = $"Basic {credentials}";
        _userManager.FindByNameAsync("nonexistentuser").Returns(Task.FromResult<TestUser?>(null));

        await handler.InitializeAsync(
            new AuthenticationScheme("Basic", null, typeof(BasicAuthenticationHandler<TestUser, string>)),
            context
        );

        // when
        var result = await handler.AuthenticateAsync();

        // then - should use same message for user not found as for wrong password
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Invalid user name or password.");
        result.Failure.Message.ToLowerInvariant().Should().NotContain("user not found");
        result.Failure.Message.ToLowerInvariant().Should().NotContain("doesn't exist");
    }

    #region Helpers

    private BasicAuthenticationHandler<TestUser, string> _CreateHandler()
    {
        return new BasicAuthenticationHandler<TestUser, string>(
            _optionsMonitor,
            LoggerFactory,
            _logger,
            UrlEncoder.Default,
            _userManager,
            _signInManager
        );
    }

    private static DefaultHttpContext _CreateContext()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        return context;
    }

    #endregion
}
