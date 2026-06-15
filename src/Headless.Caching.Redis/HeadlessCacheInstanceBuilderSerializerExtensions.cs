// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Serializer;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching;

/// <summary>
/// Serializer configuration for named Redis cache instances. Serialization is a Redis-tier concern: the value
/// segment is encoded by the configured <see cref="ISerializer"/> before it is framed and written to Redis.
/// InMemory tiers store object references and never serialize, so this surface lives on the Redis provider only.
/// </summary>
[PublicAPI]
public static class HeadlessCacheInstanceBuilderSerializerExtensions
{
    extension(HeadlessCacheInstanceBuilder instance)
    {
        /// <summary>Uses a specific serializer instance for this named Redis cache instance.</summary>
        /// <param name="serializer">The serializer used to encode this instance's value segment.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder WithSerializer(ISerializer serializer)
        {
            Argument.IsNotNull(serializer);

            return instance.WithSerializer(_ => serializer);
        }

        /// <summary>Uses a service provider-aware serializer factory for this named Redis cache instance.</summary>
        /// <param name="serializerFactory">Factory that creates or resolves the serializer.</param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder WithSerializer(Func<IServiceProvider, ISerializer> serializerFactory)
        {
            Argument.IsNotNull(serializerFactory);

            instance.SetSerializerFactory(serializerFactory);
            return instance;
        }

        /// <summary>Uses <typeparamref name="TSerializer"/> for this named Redis cache instance.</summary>
        /// <typeparam name="TSerializer">Serializer implementation type.</typeparam>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessCacheInstanceBuilder WithSerializer<TSerializer>()
            where TSerializer : class, ISerializer
        {
            return instance.WithSerializer(sp => ActivatorUtilities.GetServiceOrCreateInstance<TSerializer>(sp));
        }
    }
}
