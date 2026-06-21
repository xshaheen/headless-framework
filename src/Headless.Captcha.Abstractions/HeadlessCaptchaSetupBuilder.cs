// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Captcha;

/// <summary>
/// Root builder for <c>AddHeadlessCaptcha</c>. Provider packages contribute deferred service registrations into two
/// slots — an optional default (at most one, resolvable unkeyed plus under its canonical key) and named instances
/// (unlimited, unique names, keyed-only). Nothing is registered into <see cref="Services"/> until the setup gates
/// pass; contributions are queued only, so a throwing setup leaves the collection unchanged.
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
    /// Queues the default (unkeyed) verifier contribution, which is also aliased under <paramref name="providerKey"/>.
    /// At most one default may be registered; register additional providers with the name-taking overloads.
    /// </summary>
    /// <param name="providerKey">The provider's canonical key (one of the <see cref="CaptchaConstants"/> values).</param>
    /// <param name="action">The provider's deferred service registration action.</param>
    public void RegisterDefault(string providerKey, Action<IServiceCollection> action)
    {
        Argument.IsNotNullOrWhiteSpace(providerKey);
        Argument.IsNotNull(action);

        if (DefaultRegistrations.Count > 0)
        {
            throw new InvalidOperationException(
                "Headless.Captcha allows at most one default captcha provider. A default provider is already "
                    + "configured; register additional providers with the name-taking overloads (for example "
                    + "UseTurnstile(\"name\", …))."
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

    /// <summary>Queues a named (keyed-only) verifier contribution, resolvable through <see cref="ICaptchaProvider"/>.</summary>
    /// <param name="name">The provider instance name. Must be non-empty and not a reserved key.</param>
    /// <param name="action">The provider's deferred service registration action.</param>
    public void RegisterNamed(string name, Action<IServiceCollection> action)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(action);

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
            throw new InvalidOperationException($"A captcha provider named '{name}' is already configured.");
        }

        NamedRegistrations.Add(action);
    }
}
