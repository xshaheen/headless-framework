// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// Builder for a single named email sender inside <c>AddHeadlessEmails</c>. Provider packages contribute
/// exactly one provider per instance through <see cref="RegisterProvider"/> (called by each instance-scoped
/// <c>Use*</c> extension, for example <c>UseAzure</c>, <c>UseAwsSes</c>, <c>UseMailkit</c>,
/// <c>UseDevelopment</c>, <c>UseNoop</c>).
/// </summary>
[PublicAPI]
public sealed class HeadlessEmailInstanceBuilder
{
    internal HeadlessEmailInstanceBuilder(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>The email sender instance name. Used as the keyed-service key and the named-options name.</summary>
    public string Name { get; }

    internal Action<IServiceCollection>? Action { get; private set; }

    internal int RegistrationCount { get; private set; }

    /// <summary>Captures the provider contribution for this instance. Must be called exactly once.</summary>
    /// <param name="action">The provider's deferred service registration action.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public void RegisterProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        Action = action;
        RegistrationCount++;
    }
}
