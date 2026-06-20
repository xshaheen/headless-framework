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
    public void RegisterProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        Action = action;
        RegistrationCount++;
    }
}
