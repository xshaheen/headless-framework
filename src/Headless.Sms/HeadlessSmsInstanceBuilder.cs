// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sms;

/// <summary>
/// Configures a single named SMS sender instance inside <c>AddHeadlessSms</c>. Provider packages contribute
/// exactly one provider per instance through <see cref="RegisterProvider"/>, which each instance-scoped
/// <c>Use*</c> extension calls (for example <c>UseTwilio</c>, <c>UseAwsSns</c>, <c>UseCequens</c>,
/// <c>UseVodafone</c>, <c>UseDevelopment</c>, <c>UseNoop</c>).
/// </summary>
[PublicAPI]
public sealed class HeadlessSmsInstanceBuilder
{
    internal HeadlessSmsInstanceBuilder(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>
    /// Gets the SMS sender instance name. It is used both as the keyed-service key and as the
    /// named-options name.
    /// </summary>
    public string Name { get; }

    internal Action<IServiceCollection>? Action { get; private set; }

    /// <summary>
    /// Captures the provider contribution for this instance. Must be called exactly once.
    /// </summary>
    /// <param name="action">The delegate that registers provider services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A provider is already registered for this instance.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)] // provider-package plumbing, not an application-code API
    public void RegisterProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        if (Action is not null)
        {
            throw new InvalidOperationException($"Multiple providers were configured for named SMS sender '{Name}'.");
        }

        Action = action;
    }
}
