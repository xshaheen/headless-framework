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

    internal IList<IEncryptionProviderOptionsExtension> PreviousKeyEncryptionKeyProviders { get; } = [];

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
    /// Keeps an earlier key-encryption-key provider able to unwrap the data keys it wrapped, while the provider chosen on
    /// this builder wraps every new key. Use it to move from one provider to another (for example from
    /// <c>UseLocalKey</c> to a key service): register the old one here, run <see cref="IDataKeyManager.RewrapAsync" />
    /// until it returns 0, then remove this call.
    /// </summary>
    /// <param name="configure">
    /// Chooses the previous provider with its usual <c>Use…</c> member, such as
    /// <c>previous =&gt; previous.UseLocalKey(configuration)</c>. Each provider configured here only unwraps.
    /// </param>
    /// <returns>This builder to support method chaining.</returns>
    /// <remarks>
    /// The provider that wrapped a key is found by the provider name stored with it, so a previous provider must have a
    /// different name from the active one. To rotate keys within one provider, use that provider's own key versions.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="configure" /> chose no key-encryption key, or chose a key store or options, which belong on this
    /// builder.
    /// </exception>
    public HeadlessEncryptionSetupBuilder KeepPreviousKeyEncryptionKey(Action<HeadlessEncryptionSetupBuilder> configure)
    {
        Argument.IsNotNull(configure);

        var previous = new HeadlessEncryptionSetupBuilder(Services);
        configure(previous);

        if (
            previous.KeyStores.Count > 0
            || previous.OptionsConfigurator is not null
            || previous.PreviousKeyEncryptionKeyProviders.Count > 0
        )
        {
            throw new InvalidOperationException(
                "KeepPreviousKeyEncryptionKey only takes key-encryption-key providers; configure the key store, the "
                    + "options, and other previous providers on the main encryption builder."
            );
        }

        if (previous.KeyEncryptionKeyProviders.Count == 0)
        {
            throw new InvalidOperationException(
                "KeepPreviousKeyEncryptionKey was called without a key-encryption key; call the previous provider's "
                    + "Use… member inside it."
            );
        }

        foreach (var extension in previous.KeyEncryptionKeyProviders)
        {
            PreviousKeyEncryptionKeyProviders.Add(extension);
        }

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
