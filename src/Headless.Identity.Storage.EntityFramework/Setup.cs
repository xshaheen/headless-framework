// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

/// <summary>
/// Provides dependency-injection setup helpers for <see cref="HeadlessIdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken}"/>
/// and its nine-type-parameter variant.
/// </summary>
[PublicAPI]
public static class SetupIdentityEntityFramework
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers a <see cref="HeadlessIdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken}"/>
        /// (with the default <see cref="IdentityUserPasskey{TKey}"/> passkey type) and the full
        /// Headless service surface it requires.
        /// </summary>
        /// <typeparam name="TDbContext">The concrete Identity db context to register.</typeparam>
        /// <typeparam name="TUser">The user entity type.</typeparam>
        /// <typeparam name="TRole">The role entity type.</typeparam>
        /// <typeparam name="TKey">The primary key type used by user and role entities.</typeparam>
        /// <typeparam name="TUserClaim">The user-claim entity type.</typeparam>
        /// <typeparam name="TUserRole">The user-role link entity type.</typeparam>
        /// <typeparam name="TUserLogin">The external-login entity type.</typeparam>
        /// <typeparam name="TRoleClaim">The role-claim entity type.</typeparam>
        /// <typeparam name="TUserToken">The user authentication-token entity type.</typeparam>
        /// <param name="optionsAction">
        /// An optional action to configure the <see cref="DbContextOptionsBuilder"/> for the context.
        /// </param>
        /// <param name="configureHeadlessOptions">
        /// An optional action to configure Headless-specific context options such as audit behaviour.
        /// </param>
        /// <param name="contextLifetime">The DI lifetime for <typeparamref name="TDbContext"/> (default: Scoped).</param>
        /// <param name="optionsLifetime">The DI lifetime for the EF Core options object (default: Scoped).</param>
        /// <returns>
        /// An <see cref="IHeadlessDbContextBuilder"/> so optional event tiers (domain events, integration
        /// outbox) can be chained, matching the canonical <c>SetupEntityFramework.AddHeadlessDbContext</c>.
        /// </returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContext<
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

        /// <summary>
        /// Registers a <see cref="HeadlessIdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken}"/>
        /// (with the default <see cref="IdentityUserPasskey{TKey}"/> passkey type) and the full
        /// Headless service surface it requires. The options action receives the scoped
        /// <see cref="IServiceProvider"/> so it can resolve runtime services during configuration.
        /// </summary>
        /// <typeparam name="TDbContext">The concrete Identity db context to register.</typeparam>
        /// <typeparam name="TUser">The user entity type.</typeparam>
        /// <typeparam name="TRole">The role entity type.</typeparam>
        /// <typeparam name="TKey">The primary key type used by user and role entities.</typeparam>
        /// <typeparam name="TUserClaim">The user-claim entity type.</typeparam>
        /// <typeparam name="TUserRole">The user-role link entity type.</typeparam>
        /// <typeparam name="TUserLogin">The external-login entity type.</typeparam>
        /// <typeparam name="TRoleClaim">The role-claim entity type.</typeparam>
        /// <typeparam name="TUserToken">The user authentication-token entity type.</typeparam>
        /// <param name="optionsAction">
        /// An optional action to configure the <see cref="DbContextOptionsBuilder"/> using the
        /// scoped <see cref="IServiceProvider"/>. Receives <see langword="null"/> for the provider
        /// when called from design-time tooling.
        /// </param>
        /// <param name="configureHeadlessOptions">
        /// An optional action to configure Headless-specific context options such as audit behaviour.
        /// </param>
        /// <param name="contextLifetime">The DI lifetime for <typeparamref name="TDbContext"/> (default: Scoped).</param>
        /// <param name="optionsLifetime">The DI lifetime for the EF Core options object (default: Scoped).</param>
        /// <returns>
        /// An <see cref="IHeadlessDbContextBuilder"/> so optional event tiers (domain events, integration
        /// outbox) can be chained, matching the canonical <c>SetupEntityFramework.AddHeadlessDbContext</c>.
        /// </returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContext<
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

        /// <summary>
        /// Registers a <see cref="HeadlessIdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken,TUserPasskey}"/>
        /// with a custom passkey type and the full Headless service surface it requires.
        /// </summary>
        /// <typeparam name="TDbContext">The concrete Identity db context to register.</typeparam>
        /// <typeparam name="TUser">The user entity type.</typeparam>
        /// <typeparam name="TRole">The role entity type.</typeparam>
        /// <typeparam name="TKey">The primary key type used by user and role entities.</typeparam>
        /// <typeparam name="TUserClaim">The user-claim entity type.</typeparam>
        /// <typeparam name="TUserRole">The user-role link entity type.</typeparam>
        /// <typeparam name="TUserLogin">The external-login entity type.</typeparam>
        /// <typeparam name="TRoleClaim">The role-claim entity type.</typeparam>
        /// <typeparam name="TUserToken">The user authentication-token entity type.</typeparam>
        /// <typeparam name="TUserPasskey">The passkey entity type.</typeparam>
        /// <param name="optionsAction">
        /// An optional action to configure the <see cref="DbContextOptionsBuilder"/> for the context.
        /// </param>
        /// <param name="configureHeadlessOptions">
        /// An optional action to configure Headless-specific context options such as audit behaviour.
        /// </param>
        /// <param name="contextLifetime">The DI lifetime for <typeparamref name="TDbContext"/> (default: Scoped).</param>
        /// <param name="optionsLifetime">The DI lifetime for the EF Core options object (default: Scoped).</param>
        /// <returns>
        /// An <see cref="IHeadlessDbContextBuilder"/> so optional event tiers (domain events, integration
        /// outbox) can be chained, matching the canonical <c>SetupEntityFramework.AddHeadlessDbContext</c>.
        /// </returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContext<
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

        /// <summary>
        /// Registers a <see cref="HeadlessIdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken,TUserPasskey}"/>
        /// with a custom passkey type and the full Headless service surface it requires. The options
        /// action receives the scoped <see cref="IServiceProvider"/> so it can resolve runtime
        /// services during configuration.
        /// </summary>
        /// <typeparam name="TDbContext">The concrete Identity db context to register.</typeparam>
        /// <typeparam name="TUser">The user entity type.</typeparam>
        /// <typeparam name="TRole">The role entity type.</typeparam>
        /// <typeparam name="TKey">The primary key type used by user and role entities.</typeparam>
        /// <typeparam name="TUserClaim">The user-claim entity type.</typeparam>
        /// <typeparam name="TUserRole">The user-role link entity type.</typeparam>
        /// <typeparam name="TUserLogin">The external-login entity type.</typeparam>
        /// <typeparam name="TRoleClaim">The role-claim entity type.</typeparam>
        /// <typeparam name="TUserToken">The user authentication-token entity type.</typeparam>
        /// <typeparam name="TUserPasskey">The passkey entity type.</typeparam>
        /// <param name="optionsAction">
        /// An optional action to configure the <see cref="DbContextOptionsBuilder"/> using the
        /// scoped <see cref="IServiceProvider"/>. Receives <see langword="null"/> for the provider
        /// when called from design-time tooling.
        /// </param>
        /// <param name="configureHeadlessOptions">
        /// An optional action to configure Headless-specific context options such as audit behaviour.
        /// </param>
        /// <param name="contextLifetime">The DI lifetime for <typeparamref name="TDbContext"/> (default: Scoped).</param>
        /// <param name="optionsLifetime">The DI lifetime for the EF Core options object (default: Scoped).</param>
        /// <returns>
        /// An <see cref="IHeadlessDbContextBuilder"/> so optional event tiers (domain events, integration
        /// outbox) can be chained, matching the canonical <c>SetupEntityFramework.AddHeadlessDbContext</c>.
        /// </returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContext<
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
            var builder = services.AddHeadlessDbContextServices(configureHeadlessOptions);

            // Parity with AddHeadlessDbContext: attach DI-registered interceptors (e.g. commit coordination) via a
            // registered IDbContextOptionsConfiguration<TDbContext> so they also reach a consumer's own plain
            // AddDbContext<TDbContext>, not only this registration. EF Core does not auto-discover DI interceptors.
            services.AddDiRegisteredInterceptorsConfiguration<TDbContext>();

            services.AddDbContext<TDbContext>(
                (serviceProvider, optionsBuilder) =>
                {
                    optionsAction?.Invoke(serviceProvider, optionsBuilder);
                    optionsBuilder.AddHeadlessExtension();
                },
                contextLifetime,
                optionsLifetime
            );

            // Parity with plain HeadlessDbContext registration: expose IDbContextFactory<TDbContext> so
            // background work / IInitializer / BackgroundService can resolve a detached, scope-owning
            // context without a separate AddDbContextFactory call.
            services.TryAddSingleton<IDbContextFactory<TDbContext>, HeadlessDbContextFactory<TDbContext>>();

            return builder;
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
        services.Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
    }
}

internal sealed class HeadlessIdentityDefaultsSentinel;
