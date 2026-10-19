// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Headless.NetTopologySuite;

/// <summary>Extension methods for registering the framework's NetTopologySuite configuration.</summary>
[PublicAPI]
public static class SetupNetTopologySuite
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Makes <see cref="GeoServices.NtsGeometryServices"/> (SRID 4326, high precision, NG overlay and relate) the
        /// process-wide <see cref="NtsGeometryServices.Instance"/>, and registers it and
        /// <see cref="GeoServices.GeometryFactory"/> as singletons.
        /// </summary>
        /// <remarks>
        /// NetTopologySuite resolves its defaults through the static <see cref="NtsGeometryServices.Instance"/>, not
        /// through dependency injection, so geometries created or deserialized without an explicit factory ignore a
        /// registration alone. This call replaces that static for the whole process.
        /// </remarks>
        /// <returns>The same service collection.</returns>
        public IServiceCollection AddHeadlessNetTopologySuite()
        {
            NtsGeometryServices.Instance = GeoServices.NtsGeometryServices;

            services.TryAddSingleton(GeoServices.NtsGeometryServices);
            services.TryAddSingleton<GeometryFactory>(GeoServices.GeometryFactory);

            return services;
        }
    }
}
