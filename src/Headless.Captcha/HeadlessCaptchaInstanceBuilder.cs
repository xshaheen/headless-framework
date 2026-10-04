// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Captcha;

/// <summary>
/// Configures a single named captcha verifier instance.
/// </summary>
[PublicAPI]
public sealed class HeadlessCaptchaInstanceBuilder
{
    internal HeadlessCaptchaInstanceBuilder(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>Gets the verifier instance name used for keyed service lookup and named options.</summary>
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
            throw new InvalidOperationException(
                $"Multiple providers were configured for named captcha verifier '{Name}'."
            );
        }

        Action = action;
    }
}
