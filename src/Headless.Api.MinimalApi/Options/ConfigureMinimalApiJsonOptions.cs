// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

/// <summary>
/// <see cref="IConfigureOptions{TOptions}"/> implementation that applies Headless JSON serialization
/// conventions to <see cref="JsonOptions"/> (camel-case, enum strings, relaxed number handling, etc.)
/// and enables indented output in Development and Test environments for easier debugging.
/// </summary>
internal sealed class ConfigureMinimalApiJsonOptions(IWebHostEnvironment environment) : IConfigureOptions<JsonOptions>
{
    /// <summary>Applies Headless JSON serialization defaults to <paramref name="options"/>.</summary>
    /// <param name="options">The <see cref="JsonOptions"/> instance to configure.</param>
    public void Configure(JsonOptions options)
    {
        JsonConstants.ConfigureWebJsonOptions(options.SerializerOptions);

        // Pretty print the JSON in development for easier debugging.
        options.SerializerOptions.WriteIndented = environment.IsDevelopmentOrTest();
    }
}
