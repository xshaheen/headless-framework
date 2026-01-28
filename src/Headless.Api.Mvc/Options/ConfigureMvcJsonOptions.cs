// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

[PublicAPI]
public sealed class ConfigureMvcJsonOptions(IWebHostEnvironment environment) : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        JsonConstants.ConfigureWebJsonOptions(options.JsonSerializerOptions);

        // Pretty print the JSON in development for easier debugging.
        options.JsonSerializerOptions.WriteIndented = environment.IsDevelopmentOrTest();
    }
}
