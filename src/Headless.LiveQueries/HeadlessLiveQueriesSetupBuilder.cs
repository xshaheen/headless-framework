// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.LiveQueries;

/// <summary>
/// Configures live queries during <c>AddHeadlessLiveQueries(setup =&gt; …)</c>. Packages of the family add their own
/// members to this builder.
/// </summary>
[PublicAPI]
public sealed class HeadlessLiveQueriesSetupBuilder
{
    internal HeadlessLiveQueriesSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    // Composed in call order rather than last-write-wins, so setup split across several calls keeps every part.
    internal Action<LiveQueriesOptions>? OptionsConfigurator { get; private set; }

    /// <summary>
    /// Configures <see cref="LiveQueriesOptions" />. Repeated calls run in call order against the same instance, so
    /// a later call overrides an earlier one.
    /// </summary>
    /// <param name="configure">Delegate that mutates the options.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessLiveQueriesSetupBuilder ConfigureOptions(Action<LiveQueriesOptions> configure)
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
}
