// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Captcha;

/// <summary>
/// Configures default and named captcha providers for the application.
/// </summary>
[PublicAPI]
public sealed class HeadlessCaptchaSetupBuilder
{
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    internal HeadlessCaptchaSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal List<Action<IServiceCollection>> DefaultRegistrations { get; } = [];

    internal List<Action<IServiceCollection>> NamedRegistrations { get; } = [];

    /// <summary>
    /// Gets the names under which verifiers are resolvable through <see cref="ICaptchaProvider"/>.
    /// </summary>
    internal IReadOnlyCollection<string> RegisteredNames => _names;

    /// <summary>
    /// Registers the default verifier and aliases it under the specified canonical provider key.
    /// </summary>
    /// <param name="providerKey">The canonical provider key from <see cref="CaptchaConstants"/>.</param>
    /// <param name="action">The delegate that registers provider services.</param>
    /// <exception cref="ArgumentException"><paramref name="providerKey"/> is not a reserved framework key.</exception>
    /// <exception cref="InvalidOperationException">A default provider is already registered, or the key is already in use.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RegisterDefault(string providerKey, Action<IServiceCollection> action)
    {
        Argument.IsNotNullOrWhiteSpace(providerKey);
        Argument.IsNotNull(action);

        if (!CaptchaConstants.IsReservedProviderKey(providerKey))
        {
            throw new ArgumentException(
                $"The default captcha provider key '{providerKey}' must be a framework-reserved key (under the "
                    + "'Headless.Captcha:' namespace) so the default's canonical alias cannot collide with a "
                    + "consumer-owned keyed service. Use one of the CaptchaConstants values.",
                nameof(providerKey)
            );
        }

        if (DefaultRegistrations.Count > 0)
        {
            throw new InvalidOperationException(
                "Headless.Captcha allows at most one default captcha provider. A default provider is already "
                    + "configured; add additional providers as named instances (for example "
                    + "AddNamed(\"name\", i => i.UseTurnstile(…)))."
            );
        }

        if (!_names.Add(providerKey))
        {
            throw new InvalidOperationException(
                $"A captcha provider is already registered under the key '{providerKey}'."
            );
        }

        DefaultRegistrations.Add(action);
    }

    /// <summary>
    /// Adds a named captcha verifier instance.
    /// </summary>
    /// <param name="name">The verifier instance name.</param>
    /// <param name="configure">A delegate that configures the named provider.</param>
    /// <returns>The builder instance.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, contains only white space, or uses a reserved framework key prefix.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="name"/> is already configured, or the configuration delegate does not select exactly one provider.
    /// </exception>
    public HeadlessCaptchaSetupBuilder AddNamed(string name, Action<HeadlessCaptchaInstanceBuilder> configure)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configure);

        if (CaptchaConstants.IsReservedProviderKey(name))
        {
            throw new ArgumentException(
                $"The captcha name '{name}' is reserved for the framework's provider keys (the 'Headless.Captcha:' "
                    + "namespace). Pick a different name.",
                nameof(name)
            );
        }

        if (!_names.Add(name))
        {
            throw new InvalidOperationException($"A captcha verifier named '{name}' is already configured.");
        }

        var instance = new HeadlessCaptchaInstanceBuilder(name);
        configure(instance);

        if (instance.Action is null)
        {
            throw new InvalidOperationException(
                $"Named captcha verifier '{name}' requires exactly one provider. "
                    + "Call one of `UseReCaptchaV2`, `UseReCaptchaV3`, or `UseTurnstile`."
            );
        }

        NamedRegistrations.Add(instance.Action);

        return this;
    }
}
