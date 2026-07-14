// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
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
/// A default provider is selected by calling a provider's <c>Use{Provider}</c> member directly on this builder
/// (for example <c>setup.UseTurnstile(...)</c>); a named instance is added with <see cref="AddNamed"/>, whose
/// nested <see cref="HeadlessCaptchaInstanceBuilder"/> takes exactly one provider (for example
/// <c>setup.AddNamed("otp", i =&gt; i.UseTurnstile(...))</c>). <see cref="RegisterDefault"/> is the low-level
/// plumbing each default <c>Use*</c> member builds on; it is hidden from IntelliSense and not intended for
/// application code.
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
    [EditorBrowsable(EditorBrowsableState.Never)] // provider-package plumbing, not an application-code API
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
    /// Adds an independently-configured named captcha verifier, resolvable through <see cref="ICaptchaProvider"/> by
    /// <paramref name="name"/> or as a keyed <see cref="ICaptchaVerifier"/>. Named instances never touch the default
    /// (unkeyed) verifier.
    /// </summary>
    /// <param name="name">The verifier instance name. Must be non-empty, unique, and not a reserved framework key.</param>
    /// <param name="configure">Configuration action that selects exactly one provider for the instance.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is <see langword="null"/> or whitespace, or is a reserved framework key
    /// (under the <c>Headless.Captcha:</c> namespace, see <see cref="CaptchaConstants.IsReservedProviderKey"/>).
    /// </exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="name"/> is already configured, or when the instance selects zero or more than one
    /// provider.
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
