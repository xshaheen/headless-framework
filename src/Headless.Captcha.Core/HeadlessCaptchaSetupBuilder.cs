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
/// <remarks>
/// <see cref="RegisterDefault"/> and <see cref="RegisterNamed"/> are the public extension points provider packages
/// (in this repository and out-of-repo) build their <c>Use{Provider}</c> members on top of. They are part of the
/// package's NuGet contract — call them from a provider's <c>Use*</c> extension; consumers configure providers
/// through those <c>Use*</c> members rather than calling these directly.
/// </remarks>
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
    /// The names under which verifiers will be resolvable through <see cref="ICaptchaProvider"/> — every named
    /// instance plus a default provider's canonical key.
    /// </summary>
    internal IReadOnlyCollection<string> RegisteredNames => _names;

    /// <summary>
    /// Queues the default (unkeyed) verifier contribution, which is also aliased under <paramref name="providerKey"/>.
    /// At most one default may be registered; register additional providers with the name-taking overloads.
    /// </summary>
    /// <remarks>
    /// A public extension point for provider packages. <paramref name="providerKey"/> must be a framework-reserved
    /// key (under the <c>Headless.Captcha:</c> namespace, see <see cref="CaptchaConstants.IsReservedProviderKey"/>)
    /// so the default's canonical alias cannot collide with a consumer-owned keyed service.
    /// </remarks>
    /// <param name="providerKey">The provider's canonical key (one of the <see cref="CaptchaConstants"/> values).</param>
    /// <param name="action">The provider's deferred service registration action.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerKey"/> is not a reserved framework key.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a default provider is already registered, or the key is taken.</exception>
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
