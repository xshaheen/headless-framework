// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Framework.Api.Options;

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
