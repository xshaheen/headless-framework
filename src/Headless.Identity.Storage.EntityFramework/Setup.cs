// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

[PublicAPI]
public static class OrmEntityFrameworkIdentitySetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddHeadlessDbContext<
            TDbContext,
            TUser,
            TRole,
            TKey,
            TUserClaim,
            TUserRole,
            TUserLogin,
            TRoleClaim,
            TUserToken
        >(
            Action<DbContextOptionsBuilder>? optionsAction,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessIdentityDbContext<
                    TUser,
                    TRole,
                    TKey,
                    TUserClaim,
                    TUserRole,
                    TUserLogin,
                    TRoleClaim,
                    TUserToken
                >
            where TUser : IdentityUser<TKey>
            where TRole : IdentityRole<TKey>
            where TKey : IEquatable<TKey>
            where TUserClaim : IdentityUserClaim<TKey>
            where TUserRole : IdentityUserRole<TKey>
            where TUserLogin : IdentityUserLogin<TKey>
            where TRoleClaim : IdentityRoleClaim<TKey>
            where TUserToken : IdentityUserToken<TKey>
        {
            return services.AddHeadlessDbContext<
                TDbContext,
                TUser,
                TRole,
                TKey,
                TUserClaim,
                TUserRole,
                TUserLogin,
                TRoleClaim,
                TUserToken,
                IdentityUserPasskey<TKey>
            >((_, ob) => optionsAction?.Invoke(ob), contextLifetime, optionsLifetime);
        }

        public IServiceCollection AddHeadlessDbContext<
            TDbContext,
            TUser,
            TRole,
            TKey,
            TUserClaim,
            TUserRole,
            TUserLogin,
            TRoleClaim,
            TUserToken
        >(
            Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessIdentityDbContext<
                    TUser,
                    TRole,
                    TKey,
                    TUserClaim,
                    TUserRole,
                    TUserLogin,
                    TRoleClaim,
                    TUserToken
                >
            where TUser : IdentityUser<TKey>
            where TRole : IdentityRole<TKey>
            where TKey : IEquatable<TKey>
            where TUserClaim : IdentityUserClaim<TKey>
            where TUserRole : IdentityUserRole<TKey>
            where TUserLogin : IdentityUserLogin<TKey>
            where TRoleClaim : IdentityRoleClaim<TKey>
            where TUserToken : IdentityUserToken<TKey>
        {
            return services.AddHeadlessDbContext<
                TDbContext,
                TUser,
                TRole,
                TKey,
                TUserClaim,
                TUserRole,
                TUserLogin,
                TRoleClaim,
                TUserToken,
                IdentityUserPasskey<TKey>
            >(optionsAction, contextLifetime, optionsLifetime);
        }

        public IServiceCollection AddHeadlessDbContext<
            TDbContext,
            TUser,
            TRole,
            TKey,
            TUserClaim,
            TUserRole,
            TUserLogin,
            TRoleClaim,
            TUserToken,
            TUserPasskey
        >(
            Action<DbContextOptionsBuilder>? optionsAction,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessIdentityDbContext<
                    TUser,
                    TRole,
                    TKey,
                    TUserClaim,
                    TUserRole,
                    TUserLogin,
                    TRoleClaim,
                    TUserToken,
                    TUserPasskey
                >
            where TUser : IdentityUser<TKey>
            where TRole : IdentityRole<TKey>
            where TKey : IEquatable<TKey>
            where TUserClaim : IdentityUserClaim<TKey>
            where TUserRole : IdentityUserRole<TKey>
            where TUserLogin : IdentityUserLogin<TKey>
            where TRoleClaim : IdentityRoleClaim<TKey>
            where TUserToken : IdentityUserToken<TKey>
            where TUserPasskey : IdentityUserPasskey<TKey>
        {
            return services.AddHeadlessDbContext<
                TDbContext,
                TUser,
                TRole,
                TKey,
                TUserClaim,
                TUserRole,
                TUserLogin,
                TRoleClaim,
                TUserToken,
                TUserPasskey
            >((_, ob) => optionsAction?.Invoke(ob), contextLifetime, optionsLifetime);
        }

        public IServiceCollection AddHeadlessDbContext<
            TDbContext,
            TUser,
            TRole,
            TKey,
            TUserClaim,
            TUserRole,
            TUserLogin,
            TRoleClaim,
            TUserToken,
            TUserPasskey
        >(
            Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessIdentityDbContext<
                    TUser,
                    TRole,
                    TKey,
                    TUserClaim,
                    TUserRole,
                    TUserLogin,
                    TRoleClaim,
                    TUserToken,
                    TUserPasskey
                >
            where TUser : IdentityUser<TKey>
            where TRole : IdentityRole<TKey>
            where TKey : IEquatable<TKey>
            where TUserClaim : IdentityUserClaim<TKey>
            where TUserRole : IdentityUserRole<TKey>
            where TUserLogin : IdentityUserLogin<TKey>
            where TRoleClaim : IdentityRoleClaim<TKey>
            where TUserToken : IdentityUserToken<TKey>
            where TUserPasskey : IdentityUserPasskey<TKey>
        {
            services._ConfigureHeadlessIdentityDefaults();

            services.AddDbContext<TDbContext>(
                (serviceProvider, optionsBuilder) =>
                {
                    optionsAction?.Invoke(serviceProvider, optionsBuilder);
                    optionsBuilder.AddHeadlessExtension();
                },
                contextLifetime,
                optionsLifetime
            );

            return services;
        }
    }

    private static void _ConfigureHeadlessIdentityDefaults(this IServiceCollection services)
    {
        // Sentinel-based idempotency: AddHeadlessDbContext can be invoked once per DbContext
        // type, so multiple calls would otherwise accumulate duplicate IConfigureOptions
        // delegates on IdentityOptions. The TryAddSingleton sentinel collapses subsequent
        // calls to a no-op for the shared IdentityOptions wiring.
        if (services.Any(static d => d.ServiceType == typeof(HeadlessIdentityDefaultsSentinel)))
        {
            return;
        }

        services.TryAddSingleton<HeadlessIdentityDefaultsSentinel>();
        services.Configure<IdentityOptions>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        });
    }

    private sealed class HeadlessIdentityDefaultsSentinel;
}
