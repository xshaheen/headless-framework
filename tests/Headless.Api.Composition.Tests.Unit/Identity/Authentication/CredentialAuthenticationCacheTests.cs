// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text;
using Headless.Api.Identity;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Identity.Authentication;

/// <summary>
/// Drives the real Basic and API-key handlers through ASP.NET Core Identity's real sign-in manager, password hasher,
/// and claims factory, counting the store calls each request makes, with the credential cache off and on.
/// </summary>
public sealed class CredentialAuthenticationCacheTests : TestBase
{
    private const string _UserId = "user-7";
    private const string _UserName = "carol";
    private const string _Password = "P@ssw0rd-for-cache-tests";
    private const string _ApiKey = "api-key-7";
    private const string _OtherApiKey = "api-key-8";
    private static readonly TimeSpan _Duration = TimeSpan.FromMinutes(5);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly CacheUser _user = new() { Id = _UserId, UserName = _UserName };
    private readonly CacheUser _otherUser = new() { Id = "user-8", UserName = "dave" };
    private readonly IUserPasswordStore<CacheUser> _userStore;
    private readonly IApiKeyStore<CacheUser, string> _apiKeyStore;

    public CredentialAuthenticationCacheTests()
    {
        _user.PasswordHash = new PasswordHasher<CacheUser>().HashPassword(_user, _Password);

        _userStore = Substitute.For<IUserPasswordStore<CacheUser>, IUserRoleStore<CacheUser>>();
        _userStore
            .GetUserIdAsync(Arg.Any<CacheUser>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<CacheUser>().Id);
        _userStore
            .GetUserNameAsync(Arg.Any<CacheUser>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<CacheUser>().UserName);
        _userStore.FindByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_user);
        _userStore.GetPasswordHashAsync(_user, Arg.Any<CancellationToken>()).Returns(_user.PasswordHash);
        ((IUserRoleStore<CacheUser>)_userStore)
            .GetRolesAsync(Arg.Any<CacheUser>(), Arg.Any<CancellationToken>())
            .Returns(["admin"]);

        _apiKeyStore = Substitute.For<IApiKeyStore<CacheUser, string>>();
        _apiKeyStore.GetActiveApiKeyUserAsync(_ApiKey, Arg.Any<CancellationToken>()).Returns(_user);
        _apiKeyStore.GetActiveApiKeyUserAsync(_OtherApiKey, Arg.Any<CancellationToken>()).Returns(_otherUser);
    }

    [Fact]
    public async Task should_check_password_on_every_basic_request_when_cache_is_off()
    {
        // given
        await using var provider = _BuildProvider(cacheDuration: null);

        // when
        var first = await _AuthenticateBasicAsync(provider, _Password);
        var second = await _AuthenticateBasicAsync(provider, _Password);

        // then
        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeTrue();
        await _userStore.Received(2).GetPasswordHashAsync(_user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_look_up_key_on_every_request_when_cache_duration_is_zero()
    {
        // given
        await using var provider = _BuildProvider(cacheDuration: TimeSpan.Zero);

        // when
        var first = await _AuthenticateApiKeyAsync(provider, _ApiKey);
        var second = await _AuthenticateApiKeyAsync(provider, _ApiKey);

        // then
        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeTrue();
        await _apiKeyStore.Received(2).GetActiveApiKeyUserAsync(_ApiKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_answer_repeated_basic_credentials_without_the_store_when_cache_is_on()
    {
        // given
        await using var provider = _BuildProvider(_Duration);
        await _AuthenticateBasicAsync(provider, _Password);
        _userStore.ClearReceivedCalls();

        // when
        var result = await _AuthenticateBasicAsync(provider, _Password);

        // then
        result.Succeeded.Should().BeTrue();
        result.Ticket!.AuthenticationScheme.Should().Be(BasicAuthenticationOptions.DefaultScheme);
        result.Principal!.FindFirst(UserClaimTypes.UserId)!.Value.Should().Be(_UserId);
        result.Principal.IsInRole("admin").Should().BeTrue();
        _StoreQueries().Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_repeated_api_key_without_the_store_when_cache_is_on()
    {
        // given
        await using var provider = _BuildProvider(_Duration);
        await _AuthenticateApiKeyAsync(provider, _ApiKey);
        _userStore.ClearReceivedCalls();
        _apiKeyStore.ClearReceivedCalls();

        // when
        var result = await _AuthenticateApiKeyAsync(provider, _ApiKey);

        // then
        result.Succeeded.Should().BeTrue();
        result.Ticket!.AuthenticationScheme.Should().Be(ApiKeyAuthenticationSchemeOptions.DefaultScheme);
        result.Principal!.FindFirst(UserClaimTypes.UserId)!.Value.Should().Be(_UserId);
        _apiKeyStore.ReceivedCalls().Should().BeEmpty();
        _StoreQueries().Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_authenticating_a_revoked_key_until_the_entry_expires()
    {
        // given — the accepted trade-off: revocation reaches a cached key only when its entry expires
        await using var provider = _BuildProvider(_Duration);
        await _AuthenticateApiKeyAsync(provider, _ApiKey);
        _apiKeyStore.GetActiveApiKeyUserAsync(_ApiKey, Arg.Any<CancellationToken>()).Returns((CacheUser?)null);

        // when
        _time.Advance(_Duration - TimeSpan.FromSeconds(1));
        var beforeExpiry = await _AuthenticateApiKeyAsync(provider, _ApiKey);
        _time.Advance(TimeSpan.FromSeconds(1));
        var atExpiry = await _AuthenticateApiKeyAsync(provider, _ApiKey);

        // then
        beforeExpiry.Succeeded.Should().BeTrue();
        atExpiry.Succeeded.Should().BeFalse();
        atExpiry.None.Should().BeTrue();
    }

    [Fact]
    public async Task should_run_the_full_check_again_after_the_entry_expires_even_when_hits_kept_coming()
    {
        // given
        await using var provider = _BuildProvider(_Duration);
        await _AuthenticateBasicAsync(provider, _Password);

        // when — hits inside the lifetime do not extend it
        _time.Advance(TimeSpan.FromMinutes(4));
        await _AuthenticateBasicAsync(provider, _Password);
        _time.Advance(TimeSpan.FromMinutes(1));
        var result = await _AuthenticateBasicAsync(provider, _Password);

        // then
        result.Succeeded.Should().BeTrue();
        await _userStore.Received(2).GetPasswordHashAsync(_user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_cache_a_failed_basic_credential()
    {
        // given
        await using var provider = _BuildProvider(_Duration);

        // when
        var first = await _AuthenticateBasicAsync(provider, "wrong-password");
        var second = await _AuthenticateBasicAsync(provider, "wrong-password");

        // then — every failure reaches Identity, so it keeps counting toward lockout
        first.Succeeded.Should().BeFalse();
        second.Succeeded.Should().BeFalse();
        await _userStore.Received(2).GetPasswordHashAsync(_user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_answer_a_wrong_password_from_the_entry_of_the_right_one()
    {
        // given
        await using var provider = _BuildProvider(_Duration);
        await _AuthenticateBasicAsync(provider, _Password);

        // when
        var result = await _AuthenticateBasicAsync(provider, _Password + "x");

        // then
        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Be("Invalid user name or password.");
    }

    [Fact]
    public async Task should_give_each_api_key_its_own_principal()
    {
        // given
        await using var provider = _BuildProvider(_Duration);
        await _AuthenticateApiKeyAsync(provider, _ApiKey);
        await _AuthenticateApiKeyAsync(provider, _OtherApiKey);

        // when
        var first = await _AuthenticateApiKeyAsync(provider, _ApiKey);
        var other = await _AuthenticateApiKeyAsync(provider, _OtherApiKey);

        // then
        first.Principal!.FindFirst(UserClaimTypes.UserId)!.Value.Should().Be(_UserId);
        other.Principal!.FindFirst(UserClaimTypes.UserId)!.Value.Should().Be("user-8");
        await _apiKeyStore.Received(1).GetActiveApiKeyUserAsync(_ApiKey, Arg.Any<CancellationToken>());
        await _apiKeyStore.Received(1).GetActiveApiKeyUserAsync(_OtherApiKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_hand_each_request_its_own_principal()
    {
        // given
        await using var provider = _BuildProvider(_Duration);
        var miss = await _AuthenticateApiKeyAsync(provider, _ApiKey);
        ((ClaimsIdentity)miss.Principal!.Identity!).AddClaim(new Claim("added_by_request", "1"));

        // when
        var firstHit = await _AuthenticateApiKeyAsync(provider, _ApiKey);
        firstHit.Principal!.Identities.First().AddClaim(new Claim("added_by_request", "2"));
        var secondHit = await _AuthenticateApiKeyAsync(provider, _ApiKey);

        // then
        firstHit.Principal.Should().NotBeSameAs(secondHit.Principal);
        secondHit.Principal!.HasClaim(c => c.Type == "added_by_request").Should().BeFalse();
        secondHit.Principal.FindFirst(UserClaimTypes.UserId)!.Value.Should().Be(_UserId);
    }

    [Fact]
    public void should_key_by_scheme_and_credential_and_never_by_the_raw_secret()
    {
        // given
        using var cache = new CredentialAuthenticationCache(_time);
        using var otherProcess = new CredentialAuthenticationCache(_time);

        // when
        var key = cache.ComputeKey("ApiKey", _ApiKey);

        // then
        cache.ComputeKey("ApiKey", _ApiKey).Should().Be(key);
        cache.ComputeKey("ApiKey", _OtherApiKey).Should().NotBe(key);
        cache.ComputeKey("Basic", _ApiKey).Should().NotBe(key);
        otherProcess.ComputeKey("ApiKey", _ApiKey).Should().NotBe(key, "each process draws its own HMAC key");
        key.Should().NotContain(_ApiKey);
        cache
            .ComputeKey("ApiKey", new string('k', 1024))
            .Should()
            .NotBe(key, "a long credential takes the pooled path");
    }

    [Theory]
    [InlineData(-1, 1000)]
    [InlineData(61, 1000)]
    [InlineData(5, 0)]
    public async Task should_reject_an_invalid_cache_setting(int minutes, int maxEntries)
    {
        // given
        await using var provider = _BuildProvider(TimeSpan.FromMinutes(minutes), maxEntries);

        // when
        var basic = () =>
            provider
                .GetRequiredService<IOptionsMonitor<BasicAuthenticationOptions>>()
                .Get(BasicAuthenticationOptions.DefaultScheme);
        var apiKey = () =>
            provider
                .GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationSchemeOptions>>()
                .Get(ApiKeyAuthenticationSchemeOptions.DefaultScheme);

        // then
        basic.Should().Throw<OptionsValidationException>();
        apiKey.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(60)]
    public async Task should_accept_an_off_or_bounded_cache_setting(int? minutes)
    {
        // given
        await using var provider = _BuildProvider(minutes is null ? null : TimeSpan.FromMinutes(minutes.Value));

        // when
        var options = provider
            .GetRequiredService<IOptionsMonitor<BasicAuthenticationOptions>>()
            .Get(BasicAuthenticationOptions.DefaultScheme);

        // then
        options.CredentialCacheMaxEntries.Should().Be(1000);
    }

    // UserManager disposes its store with the request scope; every other call is a query a database would answer.
    private IEnumerable<string> _StoreQueries()
    {
        return _userStore
            .ReceivedCalls()
            .Select(call => call.GetMethodInfo().Name)
            .Where(name => !string.Equals(name, nameof(IDisposable.Dispose), StringComparison.Ordinal));
    }

    private ServiceProvider _BuildProvider(TimeSpan? cacheDuration, int maxEntries = 1000)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<IUserStore<CacheUser>>(_userStore);
        services.AddSingleton(Substitute.For<IRoleStore<IdentityRole>>());
        services.AddIdentityCore<CacheUser>().AddRoles<IdentityRole>().AddSignInManager();
        services
            .AddAuthentication()
            .AddApiKey<CacheUser, string, NoApiKeyStore>(configureOptions: options =>
            {
                options.CredentialCacheDuration = cacheDuration;
                options.CredentialCacheMaxEntries = maxEntries;
            })
            .AddBasicSchema<CacheUser, string>(configureOptions: options =>
            {
                options.CredentialCacheDuration = cacheDuration;
                options.CredentialCacheMaxEntries = maxEntries;
            });
        services.AddSingleton(_apiKeyStore);

        return services.BuildServiceProvider();
    }

    private static Task<AuthenticateResult> _AuthenticateBasicAsync(IServiceProvider provider, string password)
    {
        var header = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_UserName}:{password}"))}";

        return _AuthenticateAsync(
            provider,
            BasicAuthenticationOptions.DefaultScheme,
            request => request.Headers.Authorization = header
        );
    }

    private static Task<AuthenticateResult> _AuthenticateApiKeyAsync(IServiceProvider provider, string apiKey)
    {
        return _AuthenticateAsync(
            provider,
            ApiKeyAuthenticationSchemeOptions.DefaultScheme,
            request => request.Headers["X-Api-Key"] = apiKey
        );
    }

    private static async Task<AuthenticateResult> _AuthenticateAsync(
        IServiceProvider provider,
        string scheme,
        Action<HttpRequest> setCredential
    )
    {
        // Each request gets its own scope, as the host gives it: Identity's managers are scoped.
        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        setCredential(context.Request);

        return await context.AuthenticateAsync(scheme);
    }

    public sealed class CacheUser : IdentityUser<string>;

    // AddApiKey registers its store type as transient; the test replaces it with the shared substitute above.
    private sealed class NoApiKeyStore : IApiKeyStore<CacheUser, string>
    {
        public ValueTask<CacheUser?> GetActiveApiKeyUserAsync(
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult<CacheUser?>(null);
        }
    }
}
