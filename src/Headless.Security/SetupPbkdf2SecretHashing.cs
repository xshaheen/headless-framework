// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Security;

/// <summary>Selects PBKDF2-SHA256 as the algorithm that writes new secret hashes.</summary>
[PublicAPI]
public static class SetupPbkdf2SecretHashing
{
    extension(HeadlessSecretHasherSetupBuilder setup)
    {
        /// <summary>Selects PBKDF2-SHA256 with its default cost parameters (600,000 iterations).</summary>
        /// <returns>The same builder so calls can be chained.</returns>
        public HeadlessSecretHasherSetupBuilder UsePbkdf2Sha256()
        {
            setup.RegisterExtension(new Pbkdf2Sha256OptionsExtension(static _ => { }));

            return setup;
        }

        /// <summary>Selects PBKDF2-SHA256, binding <see cref="Pbkdf2Sha256HashOptions" /> from configuration.</summary>
        /// <param name="config">The configuration section that binds <see cref="Pbkdf2Sha256HashOptions" />.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config" /> is <see langword="null" />.</exception>
        public HeadlessSecretHasherSetupBuilder UsePbkdf2Sha256(IConfiguration config)
        {
            Argument.IsNotNull(config);

            setup.RegisterExtension(
                new Pbkdf2Sha256OptionsExtension(s => s.Configure<Pbkdf2Sha256HashOptions>(config))
            );

            return setup;
        }

        /// <summary>Selects PBKDF2-SHA256, configuring <see cref="Pbkdf2Sha256HashOptions" /> with a delegate.</summary>
        /// <param name="configure">Configures <see cref="Pbkdf2Sha256HashOptions" />.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSecretHasherSetupBuilder UsePbkdf2Sha256(Action<Pbkdf2Sha256HashOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Pbkdf2Sha256OptionsExtension(s => s.Configure(configure)));

            return setup;
        }

        /// <summary>
        /// Selects PBKDF2-SHA256, configuring <see cref="Pbkdf2Sha256HashOptions" /> with a delegate that can resolve
        /// services.
        /// </summary>
        /// <param name="configure">Configures <see cref="Pbkdf2Sha256HashOptions" /> using resolved services.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSecretHasherSetupBuilder UsePbkdf2Sha256(
            Action<Pbkdf2Sha256HashOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Pbkdf2Sha256OptionsExtension(s => s.Configure(configure)));

            return setup;
        }
    }

    private sealed class Pbkdf2Sha256OptionsExtension(Action<IServiceCollection> configure)
        : ISecretHashAlgorithmOptionsExtension
    {
        public string AlgorithmId => SecretHashAlgorithms.Pbkdf2Sha256;

        public void AddServices(IServiceCollection services)
        {
            // The algorithm and its validated options are already registered for verification.
            configure(services);
        }
    }
}
