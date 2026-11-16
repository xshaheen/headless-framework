// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Identity.Benchmarks;

/// <summary>
/// One authenticated request through the real Basic or API-key handler, ASP.NET Core Identity's real sign-in manager,
/// password hasher, and claims factory, over an in-memory user store. The store does no I/O, so the figures are the
/// CPU floor of a request: a database-backed store adds its round trips on top of every uncached request, and a cached
/// hit makes none. The first cached call fills the entry during warmup, so the cached rows measure hits.
/// </summary>
[MemoryDiagnoser]
public class CredentialAuthenticationBenchmarks
{
    private const string _ApiKey = "bench-key";
    private const string _UserName = "bench-user";
    private const string _Password = "P@ssw0rd-for-benchmarks";

    private ServiceProvider _provider = null!;
    private string _basicHeader = null!;

    [Params(BasicAuthenticationOptions.DefaultScheme, ApiKeyAuthenticationSchemeOptions.DefaultScheme)]
    public string Scheme { get; set; } = BasicAuthenticationOptions.DefaultScheme;

    /// <summary>Whether the scheme reuses a successful authentication; off is the default, uncached path.</summary>
    [Params(false, true)]
    public bool Cached { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var user = new BenchUser { Id = "user-1", UserName = _UserName };
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.PasswordHash = new PasswordHasher<BenchUser>().HashPassword(user, _Password);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserStore<BenchUser>>(_ => new InMemoryUserStore(user, ["admin", "auditor"]));
        services.AddIdentityCore<BenchUser>().AddSignInManager();
        TimeSpan? cacheDuration = Cached ? TimeSpan.FromMinutes(5) : null;
        services
            .AddAuthentication()
            .AddApiKey<BenchUser, string, InMemoryApiKeyStore>(configureOptions: options =>
                options.CredentialCacheDuration = cacheDuration
            )
            .AddBasicSchema<BenchUser, string>(configureOptions: options =>
                options.CredentialCacheDuration = cacheDuration
            );
        services.AddSingleton(user);

        _provider = services.BuildServiceProvider();
        _basicHeader = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_UserName}:{_Password}"));
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark]
    public async Task<AuthenticateResult> Authenticate()
    {
        await using var scope = _provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        if (string.Equals(Scheme, BasicAuthenticationOptions.DefaultScheme, StringComparison.Ordinal))
        {
            context.Request.Headers.Authorization = _basicHeader;
        }
        else
        {
            context.Request.Headers[ApiKeyAuthenticationSchemeOptions.HeaderName] = _ApiKey;
        }

        var result = await context.AuthenticateAsync(Scheme);

        return result.Succeeded
            ? result
            : throw new InvalidOperationException("The benchmark request did not authenticate.");
    }

    public sealed class BenchUser : IdentityUser<string>;

    private sealed class InMemoryApiKeyStore(BenchUser user) : IApiKeyStore<BenchUser, string>
    {
        public ValueTask<BenchUser?> GetActiveApiKeyUserAsync(
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult(string.Equals(apiKey, _ApiKey, StringComparison.Ordinal) ? user : null);
        }
    }

    private sealed class InMemoryUserStore(BenchUser user, IList<string> roles)
        : IUserPasswordStore<BenchUser>,
            IUserRoleStore<BenchUser>,
            IUserLockoutStore<BenchUser>,
            IUserSecurityStampStore<BenchUser>
    {
        public void Dispose() { }

        public Task<string> GetUserIdAsync(BenchUser u, CancellationToken cancellationToken) => Task.FromResult(u.Id);

        public Task<string?> GetUserNameAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.UserName);

        public Task SetUserNameAsync(BenchUser u, string? userName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> GetNormalizedUserNameAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(
            BenchUser u,
            string? normalizedName,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task<IdentityResult> CreateAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<BenchUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(userId, user.Id, StringComparison.Ordinal) ? user : null);

        public Task<BenchUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(
                string.Equals(normalizedUserName, user.NormalizedUserName, StringComparison.Ordinal) ? user : null
            );

        public Task SetPasswordHashAsync(BenchUser u, string? passwordHash, CancellationToken cancellationToken)
        {
            u.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.PasswordHash);

        public Task<bool> HasPasswordAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.PasswordHash is not null);

        public Task AddToRoleAsync(BenchUser u, string roleName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveFromRoleAsync(BenchUser u, string roleName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IList<string>> GetRolesAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(roles);

        public Task<bool> IsInRoleAsync(BenchUser u, string roleName, CancellationToken cancellationToken) =>
            Task.FromResult(roles.Contains(roleName, StringComparer.Ordinal));

        public Task<IList<BenchUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken) =>
            Task.FromResult<IList<BenchUser>>([user]);

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.LockoutEnd);

        public Task SetLockoutEndDateAsync(BenchUser u, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            u.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        public Task<int> IncrementAccessFailedCountAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(++u.AccessFailedCount);

        public Task ResetAccessFailedCountAsync(BenchUser u, CancellationToken cancellationToken)
        {
            u.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        public Task<int> GetAccessFailedCountAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.AccessFailedCount);

        public Task<bool> GetLockoutEnabledAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.LockoutEnabled);

        public Task SetLockoutEnabledAsync(BenchUser u, bool enabled, CancellationToken cancellationToken)
        {
            u.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task SetSecurityStampAsync(BenchUser u, string stamp, CancellationToken cancellationToken)
        {
            u.SecurityStamp = stamp;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecurityStampAsync(BenchUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.SecurityStamp);
    }
}
