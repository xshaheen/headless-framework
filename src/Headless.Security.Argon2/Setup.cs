// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Security;

/// <summary>Registers Argon2id as a secret-hashing algorithm.</summary>
[PublicAPI]
public static class SetupArgon2SecretHashing
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Argon2id <see cref="ISecretHashAlgorithm" />, which <see cref="SecretHasherOptions.Algorithm" />
        /// selects by default. Call it alongside <c>AddSecretHasher</c>; its cost parameters come from
        /// <see cref="SecretHasherOptions.Argon2id" />.
        /// </summary>
        /// <remarks>Idempotent: calling it more than once registers the algorithm once.</remarks>
        /// <returns>The same <see cref="IServiceCollection" /> so calls can be chained.</returns>
        public IServiceCollection AddArgon2idSecretHashing()
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretHashAlgorithm, Argon2idSecretHashAlgorithm>());

            return services;
        }
    }
}
