// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Orm.EntityFramework;

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
                TUserToken
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
}
