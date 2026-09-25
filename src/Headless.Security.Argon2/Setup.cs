// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Security;

/// <summary>Selects Argon2id as the algorithm that writes new secret hashes.</summary>
[PublicAPI]
public static class SetupArgon2SecretHashing
{
    extension(HeadlessSecretHasherSetupBuilder setup)
    {
        /// <summary>Selects Argon2id with its default cost parameters (the OWASP baseline: 19 MiB, 2 passes).</summary>
        /// <returns>The same builder so calls can be chained.</returns>
        public HeadlessSecretHasherSetupBuilder UseArgon2id()
        {
            setup.RegisterExtension(new Argon2idOptionsExtension(static _ => { }));

            return setup;
        }

        /// <summary>Selects Argon2id, binding <see cref="Argon2idHashOptions" /> from configuration.</summary>
        /// <param name="config">The configuration section that binds <see cref="Argon2idHashOptions" />.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config" /> is <see langword="null" />.</exception>
        public HeadlessSecretHasherSetupBuilder UseArgon2id(IConfiguration config)
        {
            Argument.IsNotNull(config);

            setup.RegisterExtension(new Argon2idOptionsExtension(s => s.Configure<Argon2idHashOptions>(config)));

            return setup;
        }

        /// <summary>Selects Argon2id, configuring <see cref="Argon2idHashOptions" /> with a delegate.</summary>
        /// <param name="configure">Configures <see cref="Argon2idHashOptions" />.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSecretHasherSetupBuilder UseArgon2id(Action<Argon2idHashOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Argon2idOptionsExtension(s => s.Configure(configure)));

            return setup;
        }

        /// <summary>
        /// Selects Argon2id, configuring <see cref="Argon2idHashOptions" /> with a delegate that can resolve services.
        /// </summary>
        /// <param name="configure">Configures <see cref="Argon2idHashOptions" /> using resolved services.</param>
        /// <returns>The same builder so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessSecretHasherSetupBuilder UseArgon2id(Action<Argon2idHashOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new Argon2idOptionsExtension(s => s.Configure(configure)));

            return setup;
        }
    }

    private sealed class Argon2idOptionsExtension(Action<IServiceCollection> configure)
        : ISecretHashAlgorithmOptionsExtension
    {
        public string AlgorithmId => SecretHashAlgorithms.Argon2id;

        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<Argon2idHashOptions, Argon2idHashOptionsValidator>();
            configure(services);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretHashAlgorithm, Argon2idSecretHashAlgorithm>());
        }
    }
}
