// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// Builder for a single named cache instance inside <c>AddHeadlessCaching</c>. Provider packages contribute
/// exactly one provider per instance through <see cref="RegisterProvider"/>.
/// </summary>
[PublicAPI]
public sealed class HeadlessCacheInstanceBuilder
{
    internal HeadlessCacheInstanceBuilder(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>The cache instance name. Never a reserved role key.</summary>
    public string Name { get; }

    internal ICacheProviderOptionsExtension? Extension { get; private set; }

    internal int RegistrationCount { get; private set; }

    /// <summary>Captures the provider contribution for this instance. Must be called exactly once.</summary>
    /// <param name="extension">The provider's deferred service contribution.</param>
    public void RegisterProvider(ICacheProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extension = extension;
        RegistrationCount++;
    }
}
