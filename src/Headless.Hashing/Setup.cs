// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Hashing;

/// <summary>Registration helpers for the secret hashing service.</summary>
/// <remarks>
/// <c>AddHeadlessSecretHasher</c> refuses a second call, because two calls could select two different algorithms for
/// new hashes.
/// </remarks>
[PublicAPI]
public static class SetupSecretHashing
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="ISecretHasher" /> as a singleton. The <paramref name="configure" /> callback binds the
        /// shared <see cref="SecretHasherOptions" /> and must select exactly one algorithm for new hashes with a
        /// <c>Use*</c> call: <c>UsePbkdf2Sha256</c> here, or <c>UseArgon2id</c> from <c>Headless.Hashing.Argon2</c>.
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
                    ? "The secret hasher requires exactly one algorithm for new hashes. Call `UseArgon2id` (Headless.Hashing.Argon2) or `UsePbkdf2Sha256`."
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
        // The validator goes first so the startup-validation runner, and with it the registration check, always runs
        // before the cost check that relies on the algorithm being there.
        services.AddStartupValidator<SecretHasherRegistrationValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SecretHasherCostCheckService>());

        // PBKDF2 always verifies, so a host that moved to another algorithm still accepts and upgrades old hashes.
        services.AddOptions<Pbkdf2Sha256HashOptions, Pbkdf2Sha256HashOptionsValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretHashAlgorithm, Pbkdf2Sha256SecretHashAlgorithm>());

        extension.AddServices(services);

        return services;
    }

    private static bool _IsRegistered<TService>(IServiceCollection services)
    {
        return services.Any(service => service.ServiceType == typeof(TService));
    }
}
