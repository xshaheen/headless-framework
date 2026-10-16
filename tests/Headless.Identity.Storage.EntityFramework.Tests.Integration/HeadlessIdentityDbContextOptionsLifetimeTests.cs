// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

/// <summary>
/// The Identity registrations choose the options lifetime by the same rules as <c>AddHeadlessDbContext</c>: shared
/// across scopes by default for an action that takes no provider, per scope for one that does or when asked.
/// </summary>
public sealed class HeadlessIdentityDbContextOptionsLifetimeTests : TestBase
{
    // Shared options give each scope a copy that differs only in its application provider, so sharing shows in the
    // provider extension: one instance across scopes, where options built per scope build a new one each time.
    private static bool _ShareExtensions(DbContextOptions first, DbContextOptions second) =>
        ReferenceEquals(RelationalOptionsExtension.Extract(first), RelationalOptionsExtension.Extract(second));

    private const string _ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Fact]
    public async Task should_share_options_across_scopes_when_registered_with_the_default_passkey_type()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessDbContext<
            DefaultPasskeyDbContext,
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>
        >(options => options.UseNpgsql(_ConnectionString));
        await using var provider = services.BuildServiceProvider();

        // when
        var (first, second) = await _ResolveFromTwoScopesAsync<DefaultPasskeyDbContext>(provider);

        // then
        _ShareExtensions(first, second).Should().BeTrue();
    }

    [Fact]
    public async Task should_share_options_across_scopes_when_registered_with_a_passkey_type()
    {
        // given
        var services = new ServiceCollection();
        _AddWithPasskey(services, options => options.UseNpgsql(_ConnectionString));
        await using var provider = services.BuildServiceProvider();

        // when
        var (first, second) = await _ResolveFromTwoScopesAsync<TestIdentityDbContext>(provider);

        // then
        _ShareExtensions(first, second).Should().BeTrue();
    }

    [Fact]
    public async Task should_build_options_per_scope_when_scoped_options_are_requested()
    {
        // given
        var services = new ServiceCollection();
        _AddWithPasskey(services, options => options.UseNpgsql(_ConnectionString), ServiceLifetime.Scoped);
        await using var provider = services.BuildServiceProvider();

        // when
        var (first, second) = await _ResolveFromTwoScopesAsync<TestIdentityDbContext>(provider);

        // then
        _ShareExtensions(first, second).Should().BeFalse();
    }

    [Fact]
    public async Task should_build_options_per_scope_when_the_action_receives_the_service_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessDbContext<
            TestIdentityDbContext,
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>,
            IdentityUserPasskey<string>
        >((_, options) => options.UseNpgsql(_ConnectionString));
        await using var provider = services.BuildServiceProvider();

        // when
        var (first, second) = await _ResolveFromTwoScopesAsync<TestIdentityDbContext>(provider);

        // then
        _ShareExtensions(first, second).Should().BeFalse();
    }

    private static void _AddWithPasskey(
        IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        ServiceLifetime? optionsLifetime = null
    )
    {
        services.AddHeadlessDbContext<
            TestIdentityDbContext,
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>,
            IdentityUserPasskey<string>
        >(optionsAction, optionsLifetime: optionsLifetime);
    }

    private static async Task<(DbContextOptions First, DbContextOptions Second)> _ResolveFromTwoScopesAsync<TContext>(
        ServiceProvider provider
    )
        where TContext : DbContext
    {
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        return (
            firstScope.ServiceProvider.GetRequiredService<DbContextOptions<TContext>>(),
            secondScope.ServiceProvider.GetRequiredService<DbContextOptions<TContext>>()
        );
    }

    private sealed class DefaultPasskeyDbContext(DbContextOptions options)
        : HeadlessIdentityDbContext<
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>
        >(options)
    {
        public override string DefaultSchema => "";
    }
}
