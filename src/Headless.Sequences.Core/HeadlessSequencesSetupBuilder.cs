// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sequences;

/// <summary>
/// Configures sequence policies and registers database providers during <c>AddHeadlessSequences</c> setup.
/// </summary>
[PublicAPI]
public sealed class HeadlessSequencesSetupBuilder
{
    internal HeadlessSequencesSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal Action<SequencesOptions>? OptionsConfigurator { get; private set; }

    internal IList<ISequencesProviderOptionsExtension> Extensions { get; } = [];

    /// <summary>
    /// Configures <see cref="SequencesOptions" /> callbacks.
    /// </summary>
    /// <param name="configure">A callback to configure the options.</param>
    /// <returns>This builder to support method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessSequencesSetupBuilder ConfigureOptions(Action<SequencesOptions> configure)
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

    /// <summary>Sets the default policy for counter names without explicit policies.</summary>
    /// <param name="policy">The default sequence policy.</param>
    /// <returns>This builder to support method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policy" /> is <see langword="null" />.</exception>
    public HeadlessSequencesSetupBuilder DefaultPolicy(SequencePolicy policy)
    {
        Argument.IsNotNull(policy);

        return ConfigureOptions(options => options.DefaultPolicy = policy);
    }

    /// <summary>Configures the policy for a specific counter name.</summary>
    /// <param name="name">The counter name, compared ordinally.</param>
    /// <param name="policy">The sequence policy.</param>
    /// <returns>This builder to support method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name" /> is <see langword="null" />, or <paramref name="policy" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="name" /> is empty, whitespace, exceeds maximum length, or contains non-portable characters.</exception>
    public HeadlessSequencesSetupBuilder Policy(string name, SequencePolicy policy)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.HasMaxLength(name, SequenceFieldLimits.NameMaxLength);
        Argument.IsPortableKey(name);
        Argument.IsNotNull(policy);

        return ConfigureOptions(options => options.Policies[name] = policy);
    }

    /// <summary>
    /// Registers a provider extension to add services when setup completes.
    /// </summary>
    /// <param name="extension">The provider extension.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterExtension(ISequencesProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        Extensions.Add(extension);
    }
}
