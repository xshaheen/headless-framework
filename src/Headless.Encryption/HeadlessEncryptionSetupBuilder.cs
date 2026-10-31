// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Encryption;

/// <summary>
/// Configures encryption during <c>AddHeadlessEncryption(setup =&gt; …)</c>: the key cache, exactly one key store
/// (<c>UsePostgreSql</c> or <c>UseSqlServer</c>), and exactly one key-encryption key (<c>UseLocalKey</c> or a key
/// service provider).
/// </summary>
[PublicAPI]
public sealed class HeadlessEncryptionSetupBuilder
{
    internal HeadlessEncryptionSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    // Composed in call order rather than last-write-wins, so setup split across several calls keeps every part.
    internal Action<EncryptionOptions>? OptionsConfigurator { get; private set; }

    internal IList<IEncryptionProviderOptionsExtension> KeyStores { get; } = [];

    internal IList<IEncryptionProviderOptionsExtension> KeyEncryptionKeyProviders { get; } = [];

    /// <summary>
    /// Configures <see cref="EncryptionOptions" />. Repeated calls run in call order against the same instance, so a
    /// later call overrides an earlier one.
    /// </summary>
    /// <param name="configure">A callback that mutates the options.</param>
    /// <returns>This builder to support method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessEncryptionSetupBuilder ConfigureOptions(Action<EncryptionOptions> configure)
    {
        Argument.IsNotNull(configure);

        var previous = OptionsConfigurator;
        OptionsConfigurator = previous is null
            ? configure
            : options =>
            {
                previous(options);
                configure(options);
            };

        return this;
    }

    /// <summary>
    /// Registers a key-store provider extension whose services are added when setup completes. Called by provider
    /// packages' <c>Use…</c> members rather than by application code.
    /// </summary>
    /// <param name="extension">The provider extension; it registers an <see cref="IDataKeyStore" />.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterKeyStore(IEncryptionProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        KeyStores.Add(extension);
    }

    /// <summary>
    /// Registers a key-encryption-key provider extension whose services are added when setup completes. Called by
    /// provider packages' <c>Use…</c> members rather than by application code.
    /// </summary>
    /// <param name="extension">The provider extension; it registers an <see cref="IKeyEncryptionKeyProvider" />.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterKeyEncryptionKeyProvider(IEncryptionProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        KeyEncryptionKeyProviders.Add(extension);
    }
}
