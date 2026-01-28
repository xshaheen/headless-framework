// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

[PublicAPI]
public sealed class ConfigureMinimalApiJsonOptions(IWebHostEnvironment environment) : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        JsonConstants.ConfigureWebJsonOptions(options.SerializerOptions);

        // Pretty print the JSON in development for easier debugging.
        options.SerializerOptions.WriteIndented = environment.IsDevelopmentOrTest();
    }
}
