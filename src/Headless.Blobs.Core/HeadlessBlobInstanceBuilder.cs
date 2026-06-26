// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Blobs;

/// <summary>
/// Builder for a single named blob storage instance inside <c>AddHeadlessBlobs</c>. Provider packages contribute
/// exactly one provider per instance through <see cref="RegisterProvider"/>.
/// </summary>
[PublicAPI]
public sealed class HeadlessBlobInstanceBuilder
{
    internal HeadlessBlobInstanceBuilder(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>The blob storage instance name.</summary>
    public string Name { get; }

    internal Action<IServiceCollection>? Action { get; private set; }

    internal int RegistrationCount { get; private set; }

    /// <summary>Captures the provider contribution for this instance. Must be called exactly once.</summary>
    /// <param name="action">The provider's deferred service registration action.</param>
    /// <exception cref="InvalidOperationException">A provider was already registered for this instance.</exception>
    public void RegisterProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        // Reject the second provider at the call site so the single-provider invariant is local and the first
        // contribution is never silently overwritten.
        if (RegistrationCount > 0)
        {
            throw new InvalidOperationException(
                $"Multiple providers were configured for named blob storage instance '{Name}'. "
                    + "Configure exactly one provider per named instance."
            );
        }

        Action = action;
        RegistrationCount++;
    }
}
