// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sms;

/// <summary>
/// Configures default and named SMS senders for the application.
/// </summary>
[PublicAPI]
public sealed class HeadlessSmsSetupBuilder
{
    private readonly HashSet<string> _instanceNames = new(StringComparer.Ordinal);

    internal HeadlessSmsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IReadOnlySet<string> InstanceNames => _instanceNames;

    internal List<Action<IServiceCollection>> DefaultExtensions { get; } = [];

    internal List<(string Name, Action<IServiceCollection> Action)> NamedExtensions { get; } = [];

    /// <summary>
    /// Registers the default SMS sender provider contribution.
    /// </summary>
    /// <param name="action">The delegate that registers provider services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RegisterDefaultProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        DefaultExtensions.Add(action);
    }

    /// <summary>
    /// Adds an independently configured named SMS sender instance.
    /// </summary>
    /// <param name="name">The sender instance name.</param>
    /// <param name="configure">A delegate that configures the named provider.</param>
    /// <returns>The builder instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains only white space.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="name"/> is already configured, or the configuration delegate does not select exactly one provider.
    /// </exception>
    public HeadlessSmsSetupBuilder AddNamed(string name, Action<HeadlessSmsInstanceBuilder> configure)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configure);

        if (!_instanceNames.Add(name))
        {
            throw new InvalidOperationException($"A named SMS sender '{name}' is already configured.");
        }

        var instance = new HeadlessSmsInstanceBuilder(name);
        configure(instance);

        if (instance.Action is null)
        {
            throw new InvalidOperationException(
                $"Named SMS sender '{name}' requires exactly one provider. "
                    + "Call one of `UseAwsSns`, `UseCequens`, `UseConnekio`, `UseDevelopment`, `UseInfobip`, "
                    + "`UseNoop`, `UseTwilio`, `UseVictoryLink`, or `UseVodafone`."
            );
        }

        NamedExtensions.Add((name, instance.Action));

        return this;
    }
}
