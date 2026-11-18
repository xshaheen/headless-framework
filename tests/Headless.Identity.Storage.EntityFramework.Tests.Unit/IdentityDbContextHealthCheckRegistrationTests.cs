// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class IdentityDbContextHealthCheckRegistrationTests : TestBase
{
    private static readonly string _Name = "dbcontext-" + typeof(ProbeIdentityDbContext).FullName;

    public static TheoryData<string> Overloads =>
        [
            "per-scope",
            "per-scope-with-provider",
            "per-scope-with-passkey",
            "per-scope-with-passkey-and-provider",
            "pool",
            "pool-with-provider",
            "pool-with-passkey",
            "pool-with-passkey-and-provider",
        ];

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_register_the_dbcontext_readiness_check_when_registering_the_identity_context(
        string overload
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        _Register(services, overload);
        await using var provider = services.BuildServiceProvider();

        // when
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        // then
        var registration = registrations.Should().ContainSingle(r => r.Name == _Name).Subject;
        registration
            .Tags.Should()
            .BeEquivalentTo([
                HeadlessHealthCheckTags.Ready,
                HeadlessHealthCheckTags.Headless,
                HeadlessHealthCheckTags.Database,
            ]);
    }

    private static void _Register(IServiceCollection services, string overload)
    {
        Action<DbContextOptionsBuilder> options = static _ => { };
        Action<IServiceProvider, DbContextOptionsBuilder> optionsWithProvider = static (_, _) => { };

        switch (overload)
        {
            case "per-scope":
                services.AddHeadlessDbContext<
                    ProbeIdentityDbContext,
                    User,
                    Role,
                    string,
                    UserClaim,
                    UserRole,
                    UserLogin,
                    RoleClaim,
                    UserToken
                >(options);
                break;
            case "per-scope-with-provider":
                services.AddHeadlessDbContext<
                    ProbeIdentityDbContext,
                    User,
                    Role,
                    string,
                    UserClaim,
                    UserRole,
                    UserLogin,
                    RoleClaim,
                    UserToken
                >(optionsWithProvider);
                break;
            case "per-scope-with-passkey":
                services.AddHeadlessDbContext<
                    ProbeIdentityDbContext,
                    User,
                    Role,
                    string,
                    UserClaim,
                    UserRole,
                    UserLogin,
                    RoleClaim,
                    UserToken,
                    IdentityUserPasskey<string>
                >(options);
                break;
            case "per-scope-with-passkey-and-provider":
                services.AddHeadlessDbContext<
                    ProbeIdentityDbContext,
                    User,
                    Role,
                    string,
                    UserClaim,
                    UserRole,
                    UserLogin,
                    RoleClaim,
                    UserToken,
                    IdentityUserPasskey<string>
                >(optionsWithProvider);
                break;
            case "pool":
                services.AddHeadlessDbContextPool<
                    ProbeIdentityDbContext,
                    User,
                    Role,
                    string,
                    UserClaim,
                    UserRole,
                    UserLogin,
                    RoleClaim,
                    UserToken
                >(options);
                break;
            case "pool-with-provider":
                services.AddHeadlessDbContextPool<
                    ProbeIdentityDbContext,
                    User,
                    Role,
                    string,
                    UserClaim,
                    UserRole,
                    UserLogin,
                    RoleClaim,
                    UserToken
                >(optionsWithProvider);
                break;
            case "pool-with-passkey":
                services.AddHeadlessDbContextPool<
                    ProbeIdentityDbContext,
                    User,
                    Role,
                    string,
                    UserClaim,
                    UserRole,
                    UserLogin,
                    RoleClaim,
                    UserToken,
                    IdentityUserPasskey<string>
                >(options);
                break;
            case "pool-with-passkey-and-provider":
                services.AddHeadlessDbContextPool<
                    ProbeIdentityDbContext,
                    User,
                    Role,
                    string,
                    UserClaim,
                    UserRole,
                    UserLogin,
                    RoleClaim,
                    UserToken,
                    IdentityUserPasskey<string>
                >(optionsWithProvider);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(overload), overload, message: null);
        }
    }

    private sealed class User : IdentityUser<string>;

    private sealed class Role : IdentityRole<string>;

    private sealed class UserClaim : IdentityUserClaim<string>;

    private sealed class UserRole : IdentityUserRole<string>;

    private sealed class UserLogin : IdentityUserLogin<string>;

    private sealed class RoleClaim : IdentityRoleClaim<string>;

    private sealed class UserToken : IdentityUserToken<string>;

    private sealed class ProbeIdentityDbContext(DbContextOptions options)
        : HeadlessIdentityDbContext<User, Role, string, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>(options)
    {
        public override string DefaultSchema => "";
    }
}
