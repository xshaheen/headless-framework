// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// Configures the default and named email senders for <c>AddHeadlessEmails</c>. Provider packages contribute
/// deferred service registrations into two slots: an optional default sender (at most one, the unkeyed
/// <see cref="IEmailSender"/>) and named instances (unlimited, unique names, resolved as keyed
/// <see cref="IEmailSender"/> services or through <see cref="IEmailSenderProvider"/>). Nothing is registered
/// into <see cref="Services"/> until the setup gates pass; contributions are queued only.
/// </summary>
/// <remarks>
/// Emails has no shared, cross-provider feature options, so the builder is provider-selection-only and
/// carries no <c>Configure</c> overloads. Each provider binds its own options inside its <c>Use*</c> member.
/// </remarks>
[PublicAPI]
public sealed class HeadlessEmailsSetupBuilder
{
    private readonly HashSet<string> _instanceNames = new(StringComparer.Ordinal);

    internal HeadlessEmailsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IReadOnlySet<string> InstanceNames => _instanceNames;

    internal List<Action<IServiceCollection>> DefaultExtensions { get; } = [];

    internal List<(string Name, Action<IServiceCollection> Action)> NamedExtensions { get; } = [];

    /// <summary>
    /// Queues the default (unkeyed) email sender contribution. Each default <c>Use*</c> extension calls this
    /// internally; it is not intended for direct use by application code.
    /// </summary>
    /// <param name="action">The delegate that registers provider services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)] // provider-package plumbing, not an application-code API
    public void RegisterDefaultProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        DefaultExtensions.Add(action);
    }

    /// <summary>
    /// Adds an independently configured named email sender, resolvable as a keyed <see cref="IEmailSender"/>
    /// service or through <see cref="IEmailSenderProvider"/>. Named instances never touch the default
    /// (unkeyed) <see cref="IEmailSender"/>.
    /// </summary>
    /// <param name="name">The sender instance name. Must be non-empty and unique within this setup.</param>
    /// <param name="configure">A delegate that selects exactly one provider for the instance.</param>
    /// <returns>The builder instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains only white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="name"/> is already configured, or the configuration delegate selects zero or more
    /// than one provider.
    /// </exception>
    public HeadlessEmailsSetupBuilder AddNamed(string name, Action<HeadlessEmailInstanceBuilder> configure)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configure);

        if (!_instanceNames.Add(name))
        {
            throw new InvalidOperationException($"A named email sender '{name}' is already configured.");
        }

        var instance = new HeadlessEmailInstanceBuilder(name);
        configure(instance);

        if (instance.Action is null)
        {
            throw new InvalidOperationException(
                $"Named email sender '{name}' requires exactly one provider. "
                    + "Call one of `UseAzure`, `UseAwsSes`, `UseMailkit`, `UseDevelopment`, or `UseNoop`."
            );
        }

        NamedExtensions.Add((name, instance.Action));

        return this;
    }
}
