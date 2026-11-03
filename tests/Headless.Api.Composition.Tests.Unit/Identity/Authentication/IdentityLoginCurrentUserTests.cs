// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Api;
using Headless.Api.Identity;
using Headless.Context;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Identity.Authentication;

/// <summary>
/// Drives the real Basic and API-key handlers through ASP.NET Core Identity's real sign-in manager and claims
/// factory, then reads the resulting principal the way the web host's <see cref="ICurrentUser"/> does.
/// </summary>
public sealed class IdentityLoginCurrentUserTests : TestBase
{
    private const string _UserId = "user-42";
    private const string _UserName = "alice";
    private const string _Password = "P@ssw0rd-for-tests";
    private const string _ApiKey = "api-key-42";
    private static readonly string[] _Roles = ["admin", "auditor"];

    [Fact]
    public async Task should_expose_user_id_and_roles_to_current_user_when_api_key_login_succeeds()
    {
        // given
        await using var provider = _BuildProvider();
        var context = _CreateContext(provider);
        context.Request.Headers["X-Api-Key"] = _ApiKey;

        // when
        var result = await context.AuthenticateAsync(ApiKeyAuthenticationSchemeOptions.DefaultScheme);

        // then
        result.Succeeded.Should().BeTrue();
        _AssertCurrentUser(result.Principal!);
    }

    [Fact]
    public async Task should_expose_user_id_and_roles_to_current_user_when_basic_login_succeeds()
    {
        // given
        await using var provider = _BuildProvider();
        var context = _CreateContext(provider);
        context.Request.Headers.Authorization =
            $"Basic {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_UserName}:{_Password}"))}";

        // when
        var result = await context.AuthenticateAsync(BasicAuthenticationOptions.DefaultScheme);

        // then
        result.Succeeded.Should().BeTrue();
        _AssertCurrentUser(result.Principal!);
    }

    [Fact]
    public async Task should_keep_consumer_claim_types_when_set_explicitly()
    {
        // given — a consumer that chose its own claim types keeps them, whichever registration ran first
        await using var provider = _BuildProvider(identity =>
        {
            identity.ClaimsIdentity.UserIdClaimType = "custom_uid";
            identity.ClaimsIdentity.RoleClaimType = "custom_role";
        });

        // when
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        // then
        options.ClaimsIdentity.UserIdClaimType.Should().Be("custom_uid");
        options.ClaimsIdentity.RoleClaimType.Should().Be("custom_role");
    }

    private static void _AssertCurrentUser(ClaimsPrincipal principal)
    {
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var currentUser = new HttpCurrentUser(accessor);

        currentUser.IsAuthenticated.Should().BeTrue();
        currentUser.UserId.Should().NotBeNull();
        currentUser.UserId!.ToString().Should().Be(_UserId);
        currentUser.Roles.Should().BeEquivalentTo(_Roles);
        principal.IsInRole("admin").Should().BeTrue();
    }

    private static ServiceProvider _BuildProvider(Action<IdentityOptions>? configureIdentity = null)
    {
        var user = new LoginUser { Id = _UserId, UserName = _UserName };
        user.PasswordHash = new PasswordHasher<LoginUser>().HashPassword(user, _Password);

        var userStore = Substitute.For<IUserPasswordStore<LoginUser>, IUserRoleStore<LoginUser>>();
        userStore.GetUserIdAsync(user, Arg.Any<CancellationToken>()).Returns(_UserId);
        userStore.GetUserNameAsync(user, Arg.Any<CancellationToken>()).Returns(_UserName);
        userStore.FindByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        userStore.GetPasswordHashAsync(user, Arg.Any<CancellationToken>()).Returns(user.PasswordHash);
        ((IUserRoleStore<LoginUser>)userStore).GetRolesAsync(user, Arg.Any<CancellationToken>()).Returns(_Roles);

        var apiKeyStore = new LoginApiKeyStore(user);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserStore<LoginUser>>(userStore);
        services.AddSingleton(Substitute.For<IRoleStore<IdentityRole>>());

        // The consumer's Identity setup runs before the schemes here; the reverse order is the natural one for
        // AddAuthentication-first hosts and must not matter either.
        services
            .AddIdentityCore<LoginUser>(options => configureIdentity?.Invoke(options))
            .AddRoles<IdentityRole>()
            .AddSignInManager();

        services
            .AddAuthentication()
            .AddApiKey<LoginUser, string, LoginApiKeyStore>()
            .AddBasicSchema<LoginUser, string>();
        services.AddSingleton<IApiKeyStore<LoginUser, string>>(apiKeyStore);

        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext _CreateContext(IServiceProvider provider)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        return context;
    }

    public sealed class LoginUser : IdentityUser<string>;

    private sealed class LoginApiKeyStore(LoginUser user) : IApiKeyStore<LoginUser, string>
    {
        public ValueTask<LoginUser?> GetActiveApiKeyUserAsync(
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult(string.Equals(apiKey, _ApiKey, StringComparison.Ordinal) ? user : null);
        }
    }
}
