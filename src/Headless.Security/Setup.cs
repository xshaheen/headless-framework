// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Security;

/// <summary>Registration helpers for the string encryption, lookup hashing, and secret hashing services.</summary>
/// <remarks>
/// <c>AddStringEncryptionService</c> and <c>AddLookupHasher</c> are idempotent: the first registration wins, and a
/// later call with different options is silently ignored. <c>AddHeadlessSecretHasher</c> instead refuses a second
/// call, because two calls could select two different algorithms for new hashes.
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
        /// Registers <see cref="ISecretHasher" /> as a singleton. The <paramref name="configure" /> callback binds the
        /// shared <see cref="SecretHasherOptions" /> and must select exactly one algorithm for new hashes with a
        /// <c>Use*</c> call: <c>UsePbkdf2Sha256</c> here, or <c>UseArgon2id</c> from <c>Headless.Security.Argon2</c>.
        /// </summary>
        /// <remarks>
        /// PBKDF2-SHA256 is always registered for verification, whichever algorithm writes, so stored PBKDF2 hashes keep
        /// verifying and are upgraded through <see cref="SecretVerification.Rehashed" />.
        /// </remarks>
        /// <param name="configure">Configures the shared options and selects the algorithm.</param>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No algorithm, or more than one, was selected, or the secret hasher is already registered.
        /// </exception>
        public IServiceCollection AddHeadlessSecretHasher(Action<HeadlessSecretHasherSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessSecretHasherSetupBuilder(services);
            configure(setup);

            return _AddSecretHasherCore(services, setup);
        }
    }

    private static IServiceCollection _AddSecretHasherCore(
        IServiceCollection services,
        HeadlessSecretHasherSetupBuilder setup
    )
    {
        if (setup.Extensions.Count != 1)
        {
            throw new InvalidOperationException(
                setup.Extensions.Count == 0
                    ? "The secret hasher requires exactly one algorithm for new hashes. Call `UseArgon2id` (Headless.Security.Argon2) or `UsePbkdf2Sha256`."
                    : "The secret hasher requires exactly one algorithm for new hashes. Multiple algorithms were selected."
            );
        }

        // A second registration would silently leave two competing algorithm selections; refuse it instead.
        if (_IsRegistered<ISecretHasher>(services))
        {
            throw new InvalidOperationException(
                "AddHeadlessSecretHasher was already called on this service collection."
            );
        }

        var extension = setup.Extensions[0];

        services.AddOptions<SecretHasherOptions, SecretHasherOptionsValidator>();
        services.AddSingleton(new SecretHasherAlgorithmSelection(extension.AlgorithmId));
        services.TryAddSingleton<ISecretHasher, SecretHasher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SecretHasherStartupValidationService>());

        // PBKDF2 always verifies, so a host that moved to another algorithm still accepts and upgrades old hashes.
        services.AddOptions<Pbkdf2Sha256HashOptions, Pbkdf2Sha256HashOptionsValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretHashAlgorithm, Pbkdf2Sha256SecretHashAlgorithm>());

        extension.AddServices(services);

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
