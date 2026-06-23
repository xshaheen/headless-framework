// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Serializer;
using Microsoft.Extensions.DependencyInjection;

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

    internal Action<IServiceCollection>? Action { get; private set; }

    internal Func<IServiceProvider, ISerializer>? SerializerFactory { get; private set; }

    internal void SetSerializerFactory(Func<IServiceProvider, ISerializer> serializerFactory)
    {
        Argument.IsNotNull(serializerFactory);

        if (SerializerFactory is not null)
        {
            throw new InvalidOperationException(
                $"A serializer is already configured for named cache instance '{Name}'."
            );
        }

        SerializerFactory = serializerFactory;
    }

    /// <summary>Captures the provider contribution for this instance. Must be called exactly once.</summary>
    /// <param name="action">The provider's deferred service registration action.</param>
    /// <exception cref="InvalidOperationException">Thrown when a provider is already registered for this instance.</exception>
    public void RegisterProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        if (Action is not null)
        {
            throw new InvalidOperationException(
                $"Multiple providers were configured for named cache instance '{Name}'."
            );
        }

        Action = action;
    }
}
