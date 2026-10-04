// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// Configures a single named email sender instance.
/// </summary>
[PublicAPI]
public sealed class HeadlessEmailInstanceBuilder
{
    internal HeadlessEmailInstanceBuilder(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>Gets the email sender instance name used for keyed service resolution.</summary>
    public string Name { get; }

    internal Action<IServiceCollection>? Action { get; private set; }

    /// <summary>Registers the provider contribution for this instance.</summary>
    /// <param name="action">The delegate that registers provider services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A provider is already registered for this instance.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RegisterProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        if (Action is not null)
        {
            throw new InvalidOperationException($"Multiple providers were configured for named email sender '{Name}'.");
        }

        Action = action;
    }
}
