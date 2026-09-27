// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Security;

/// <summary>
/// Builder passed to <c>AddHeadlessSecretHasher</c>. Binds the shared <see cref="SecretHasherOptions" /> and selects
/// exactly one algorithm for new hashes through a <c>Use*</c> extension, such as <c>UsePbkdf2Sha256</c> or
/// <c>UseArgon2id</c> from <c>Headless.Security.Argon2</c>.
/// </summary>
[PublicAPI]
public sealed class HeadlessSecretHasherSetupBuilder
{
    internal HeadlessSecretHasherSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<ISecretHashAlgorithmOptionsExtension> Extensions { get; } = [];

    /// <summary>Binds <see cref="SecretHasherOptions" /> from the supplied configuration section.</summary>
    /// <param name="configuration">The configuration section to bind from.</param>
    /// <returns>The same builder so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
    public HeadlessSecretHasherSetupBuilder Configure(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        // Validation is attached once by AddHeadlessSecretHasher, however many Configure calls there are.
        Services.Configure<SecretHasherOptions>(configuration);

        return this;
    }

    /// <summary>Configures <see cref="SecretHasherOptions" /> with the supplied delegate.</summary>
    /// <param name="configure">Delegate that mutates the options.</param>
    /// <returns>The same builder so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessSecretHasherSetupBuilder Configure(Action<SecretHasherOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure(configure);

        return this;
    }

    /// <summary>Configures <see cref="SecretHasherOptions" /> with a delegate that can resolve services.</summary>
    /// <param name="configure">Delegate that mutates the options using the service provider.</param>
    /// <returns>The same builder so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessSecretHasherSetupBuilder Configure(Action<SecretHasherOptions, IServiceProvider> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure(configure);

        return this;
    }

    /// <summary>
    /// Registers the algorithm that writes new hashes. Called by each <c>Use*</c> extension; not intended for direct use
    /// by application code.
    /// </summary>
    /// <param name="extension">The algorithm extension.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterExtension(ISecretHashAlgorithmOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
