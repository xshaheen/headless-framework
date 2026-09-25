// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Security;

/// <summary>Registration helpers for the string encryption, lookup hashing, and secret hashing services.</summary>
/// <remarks>
/// All <c>Add*</c> members are idempotent: the first registration for a given service wins, and a later call with
/// different options is silently ignored. Configure each service once.
/// </remarks>
[PublicAPI]
public static class SetupSecurity
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IStringEncryptionService" /> as a singleton, binding
        /// <see cref="StringEncryptionOptions" /> from the supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that binds <see cref="StringEncryptionOptions" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config" /> is <see langword="null" />.</exception>
        public IServiceCollection AddStringEncryptionService(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _AddEncryptionCore(
                services,
                s => s.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers <see cref="IStringEncryptionService" /> as a singleton, configuring
        /// <see cref="StringEncryptionOptions" /> with the supplied delegate.
        /// </summary>
        /// <param name="configure">Configures <see cref="StringEncryptionOptions" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddStringEncryptionService(Action<StringEncryptionOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _AddEncryptionCore(
                services,
                s => s.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers <see cref="IStringEncryptionService" /> as a singleton, configuring
        /// <see cref="StringEncryptionOptions" /> with the supplied delegate that can resolve services from the
        /// <see cref="IServiceProvider" />.
        /// </summary>
        /// <param name="configure">Configures <see cref="StringEncryptionOptions" /> using resolved services.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddStringEncryptionService(
            Action<StringEncryptionOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            return _AddEncryptionCore(
                services,
                s => s.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers <see cref="ILookupHasher" /> as a singleton, binding <see cref="LookupHasherOptions" /> from
        /// the supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that binds <see cref="LookupHasherOptions" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config" /> is <see langword="null" />.</exception>
        public IServiceCollection AddLookupHasher(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _AddLookupHasherCore(
                services,
                s => s.Configure<LookupHasherOptions, LookupHasherOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers <see cref="ILookupHasher" /> as a singleton, configuring <see cref="LookupHasherOptions" />
        /// with the supplied delegate.
        /// </summary>
        /// <param name="configure">Configures <see cref="LookupHasherOptions" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddLookupHasher(Action<LookupHasherOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _AddLookupHasherCore(
                services,
                s => s.Configure<LookupHasherOptions, LookupHasherOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers <see cref="ILookupHasher" /> as a singleton, configuring <see cref="LookupHasherOptions" />
        /// with the supplied delegate that can resolve services from the <see cref="IServiceProvider" />.
        /// </summary>
        /// <param name="configure">Configures <see cref="LookupHasherOptions" /> using resolved services.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddLookupHasher(Action<LookupHasherOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _AddLookupHasherCore(
                services,
                s => s.Configure<LookupHasherOptions, LookupHasherOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers <see cref="ISecretHasher" /> as a singleton with the built-in PBKDF2-SHA256 algorithm, binding
        /// <see cref="SecretHasherOptions" /> from the supplied configuration section.
        /// </summary>
        /// <remarks>
        /// The default <see cref="SecretHasherOptions.Algorithm" /> is Argon2id, which also needs
        /// <c>AddArgon2idSecretHashing()</c> from the <c>Headless.Security.Argon2</c> package; without it, host startup
        /// fails with a message naming that package.
        /// </remarks>
        /// <param name="config">The configuration section that binds <see cref="SecretHasherOptions" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config" /> is <see langword="null" />.</exception>
        public IServiceCollection AddSecretHasher(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _AddSecretHasherCore(
                services,
                s => s.Configure<SecretHasherOptions, SecretHasherOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers <see cref="ISecretHasher" /> as a singleton with the built-in PBKDF2-SHA256 algorithm, configuring
        /// <see cref="SecretHasherOptions" /> with the supplied delegate.
        /// </summary>
        /// <remarks>
        /// The default <see cref="SecretHasherOptions.Algorithm" /> is Argon2id, which also needs
        /// <c>AddArgon2idSecretHashing()</c> from the <c>Headless.Security.Argon2</c> package; without it, host startup
        /// fails with a message naming that package.
        /// </remarks>
        /// <param name="configure">Configures <see cref="SecretHasherOptions" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddSecretHasher(Action<SecretHasherOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _AddSecretHasherCore(
                services,
                s => s.Configure<SecretHasherOptions, SecretHasherOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers <see cref="ISecretHasher" /> as a singleton with the built-in PBKDF2-SHA256 algorithm, configuring
        /// <see cref="SecretHasherOptions" /> with the supplied delegate that can resolve services from the
        /// <see cref="IServiceProvider" />.
        /// </summary>
        /// <remarks>
        /// The default <see cref="SecretHasherOptions.Algorithm" /> is Argon2id, which also needs
        /// <c>AddArgon2idSecretHashing()</c> from the <c>Headless.Security.Argon2</c> package; without it, host startup
        /// fails with a message naming that package.
        /// </remarks>
        /// <param name="configure">Configures <see cref="SecretHasherOptions" /> using resolved services.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddSecretHasher(Action<SecretHasherOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _AddSecretHasherCore(
                services,
                s => s.Configure<SecretHasherOptions, SecretHasherOptionsValidator>(configure)
            );
        }
    }

    private static IServiceCollection _AddSecretHasherCore(IServiceCollection services, Action<IServiceCollection> bind)
    {
        if (_IsRegistered<ISecretHasher>(services))
        {
            return services;
        }

        bind(services);
        services.TryAddSingleton<ISecretHasher, SecretHasher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretHashAlgorithm, Pbkdf2Sha256SecretHashAlgorithm>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SecretHasherStartupValidationService>());

        return services;
    }

    private static IServiceCollection _AddEncryptionCore(IServiceCollection services, Action<IServiceCollection> bind)
    {
        if (_IsRegistered<IStringEncryptionService>(services))
        {
            return services;
        }

        bind(services);
        services.AddSingletonOptionValue<StringEncryptionOptions>();
        services.TryAddSingleton<IStringEncryptionService, StringEncryptionService>();

        return services;
    }

    private static IServiceCollection _AddLookupHasherCore(IServiceCollection services, Action<IServiceCollection> bind)
    {
        if (_IsRegistered<ILookupHasher>(services))
        {
            return services;
        }

        bind(services);
        services.AddSingletonOptionValue<LookupHasherOptions>();
        services.TryAddSingleton<ILookupHasher, LookupHasher>();

        return services;
    }

    private static bool _IsRegistered<TService>(IServiceCollection services)
    {
        return services.Any(service => service.ServiceType == typeof(TService));
    }
}
