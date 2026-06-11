// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

[PublicAPI]
public static class SetupIdentityEntityFramework
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
            Action<HeadlessDbContextOptions>? configureHeadlessOptions = null,
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
            >((_, ob) => optionsAction?.Invoke(ob), configureHeadlessOptions, contextLifetime, optionsLifetime);
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
            Action<HeadlessDbContextOptions>? configureHeadlessOptions = null,
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
            >(optionsAction, configureHeadlessOptions, contextLifetime, optionsLifetime);
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
            Action<HeadlessDbContextOptions>? configureHeadlessOptions = null,
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
            >((_, ob) => optionsAction?.Invoke(ob), configureHeadlessOptions, contextLifetime, optionsLifetime);
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
            Action<HeadlessDbContextOptions>? configureHeadlessOptions = null,
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
            // Identity defaults first so the IdentityOptions configurator is in place before any
            // hosted service or option-validation pass observes it.
            services._ConfigureHeadlessIdentityDefaults();

            // Wire the full headless service surface that HeadlessIdentityDbContext requires —
            // HeadlessDbContextServices (scoped, injected into the ctor), save-changes pipeline,
            // audit persistence, ambient transaction accessor, change capture, message dispatcher,
            // tenancy guard options, clock, current tenant/user, correlation ID. The canonical
            // SetupEntityFramework helper centralizes this so Identity stays parity with plain
            // HeadlessDbContext registration.
            services.AddHeadlessDbContextServices(configureHeadlessOptions);

            services.AddDbContext<TDbContext>(
                (serviceProvider, optionsBuilder) =>
                {
                    optionsAction?.Invoke(serviceProvider, optionsBuilder);

                    // Parity with AddHeadlessDbContext: EF Core does not auto-discover IInterceptor
                    // registrations from the application container; apply them here so package-registered
                    // interceptors (e.g. commit coordination) fire for Identity contexts too.
                    optionsBuilder.AddDiRegisteredInterceptors(serviceProvider);

                    optionsBuilder.AddHeadlessExtension();
                },
                contextLifetime,
                optionsLifetime
            );

            // Parity with plain HeadlessDbContext registration: expose IDbContextFactory<TDbContext> so
            // background work / IInitializer / BackgroundService can resolve a detached, scope-owning
            // context without a separate AddDbContextFactory call.
            services.TryAddSingleton<IDbContextFactory<TDbContext>, HeadlessDbContextFactory<TDbContext>>();

            return services;
        }
    }

    private static void _ConfigureHeadlessIdentityDefaults(this IServiceCollection services)
    {
        // Sentinel-based idempotency: repeated AddHeadlessDbContext calls (e.g., multiple Identity
        // contexts in one host) configure IdentityOptions exactly once. The delegate is itself
        // idempotent (writes a fixed value), so multiple registrations are wasteful rather than
        // wrong — the sentinel keeps startup cost flat.
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
}

internal sealed class HeadlessIdentityDefaultsSentinel;
