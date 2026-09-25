// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sequences;

/// <summary>
/// Configures sequences during <c>AddHeadlessSequences(setup =&gt; …)</c>: the numbering policies, and exactly one
/// provider chosen through a <c>Use…</c> call such as <c>UsePostgreSql</c> or <c>UseSqlServer</c>.
/// </summary>
[PublicAPI]
public sealed class HeadlessSequencesSetupBuilder
{
    internal HeadlessSequencesSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    // Composed in call order rather than last-write-wins, so setup split across several calls keeps every part.
    internal Action<SequencesOptions>? OptionsConfigurator { get; private set; }

    internal IList<ISequencesProviderOptionsExtension> Extensions { get; } = [];

    /// <summary>
    /// Configures <see cref="SequencesOptions" />. Repeated calls run in call order against the same instance, so
    /// a later call overrides an earlier one.
    /// </summary>
    /// <param name="configure">Delegate that mutates the options.</param>
    /// <returns>This builder, to allow chaining.</returns>
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

    /// <summary>Sets the policy of every counter name that has no policy of its own.</summary>
    /// <param name="policy">The default policy.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policy" /> is <see langword="null" />.</exception>
    public HeadlessSequencesSetupBuilder DefaultPolicy(SequencePolicy policy)
    {
        Argument.IsNotNull(policy);

        return ConfigureOptions(options => options.DefaultPolicy = policy);
    }

    /// <summary>Sets the policy of one counter name, replacing any earlier policy for that name.</summary>
    /// <param name="name">The counter name, compared ordinally.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name" /> is blank, too long, or starts or ends with whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="policy" /> is <see langword="null" />.</exception>
    public HeadlessSequencesSetupBuilder Policy(string name, SequencePolicy policy)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.HasMaxLength(name, SequenceFieldLimits.NameMaxLength);
        SequenceKeyText.EnsureNoSurroundingWhitespace(name, "name", nameof(name));
        Argument.IsNotNull(policy);

        return ConfigureOptions(options => options.Policies[name] = policy);
    }

    /// <summary>
    /// Registers a provider extension whose services are added when setup completes. Called by provider packages'
    /// <c>Use…</c> members rather than by application code.
    /// </summary>
    /// <param name="extension">The provider extension.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterExtension(ISequencesProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        Extensions.Add(extension);
    }
}
