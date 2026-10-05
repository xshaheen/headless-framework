// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Api.Features;

/// <summary>
/// Service-collection extensions that enforce <see cref="RequiresFeatureAttribute"/> on ASP.NET Core controller
/// actions. Gate Minimal API endpoints and route groups with <c>RequireFeatures(...)</c>.
/// </summary>
/// <remarks>
/// The gates read feature values through <see cref="IFeatureManager"/>, so the host must also call
/// <c>AddHeadlessFeatures(...)</c> with a storage provider. The host fails at startup when it is missing.
/// </remarks>
[PublicAPI]
public static class SetupHttpFeatures
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds a global MVC resource filter that enforces <see cref="RequiresFeatureAttribute"/> on controllers and
        /// actions, honoring <see cref="DisableFeatureCheckAttribute"/>. Calling it more than once adds the filter once.
        /// </summary>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <remarks>
        /// A disabled feature throws <see cref="Headless.ConflictException"/> before model binding, which the Headless
        /// exception handler returns as a 409 problem response. Minimal API endpoints are not affected by this call;
        /// gate them with <c>RequireFeatures(...)</c>.
        /// </remarks>
        public IServiceCollection AddHeadlessHttpFeatures()
        {
            // Keyed on the implementation type, so a repeated call does not add a second filter that checks every
            // requirement twice.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, RequiresFeatureMvcOptionsSetup>()
            );

            // This package references only the feature abstractions: the manager ships in Headless.Features, which
            // the host registers. Without it every gated request would fail.
            services.RequireRegisteredService<IFeatureManager>(
                requiredBy: "Headless API feature gates",
                remedy: "Call AddHeadlessFeatures(...) with a storage provider (UseEntityFramework / UsePostgreSql / "
                    + "UseSqlServer)."
            );

            return services;
        }
    }

    private sealed class RequiresFeatureMvcOptionsSetup : IConfigureOptions<MvcOptions>
    {
        public void Configure(MvcOptions options)
        {
            options.Filters.Add(new RequiresFeatureResourceFilter());
        }
    }
}
