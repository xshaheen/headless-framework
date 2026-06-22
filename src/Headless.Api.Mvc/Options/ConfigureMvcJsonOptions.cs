// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

/// <summary>
/// Configures <see cref="JsonOptions"/> for MVC with Headless JSON serialization defaults.
/// </summary>
/// <remarks>
/// Applied automatically by <see cref="SetupMvc.ConfigureMvc(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// Delegates to <c>JsonConstants.ConfigureWebJsonOptions</c> (camel-case property names, string enum
/// serialization, etc.) and additionally enables indented JSON output in Development and Test environments
/// for easier debugging.
/// </remarks>
[PublicAPI]
public sealed class ConfigureMvcJsonOptions(IWebHostEnvironment environment) : IConfigureOptions<JsonOptions>
{
    /// <summary>Applies Headless JSON serialization defaults to <paramref name="options"/>.</summary>
    /// <param name="options">The <see cref="JsonOptions"/> instance to configure.</param>
    public void Configure(JsonOptions options)
    {
        JsonConstants.ConfigureWebJsonOptions(options.JsonSerializerOptions);

        // Pretty print the JSON in development for easier debugging.
        options.JsonSerializerOptions.WriteIndented = environment.IsDevelopmentOrTest();
    }
}
