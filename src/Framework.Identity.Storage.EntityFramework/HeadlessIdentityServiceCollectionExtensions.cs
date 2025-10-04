// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Orm.EntityFramework.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Orm.EntityFramework;

[PublicAPI]
public static class HeadlessIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddHeadlessDbContext<
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
        this IServiceCollection services,
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

    public static IServiceCollection AddHeadlessDbContext<
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
        this IServiceCollection services,
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
        services.TryAddSingleton<IClock, Clock>();
        services.TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
        services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.TryAddSingleton<ICurrentUser, NullCurrentUser>();

        services.TryAddSingleton<IHeadlessEntityModelProcessor, HeadlessEntityModelProcessor>();

        services.AddDbContext<TDbContext>(
            (serviceProvider, optionsBuilder) =>
            {
                optionsAction?.Invoke(serviceProvider, optionsBuilder);
                optionsBuilder.AddHeadlessDbContextOptionsExtension();
            },
            contextLifetime,
            optionsLifetime
        );

        return services;
    }
}
