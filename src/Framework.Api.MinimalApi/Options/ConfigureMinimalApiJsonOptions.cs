// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Framework.Api.MinimalApi.Options;

[PublicAPI]
public sealed class ConfigureMinimalApiJsonOptions(IWebHostEnvironment environment) : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        FrameworkJsonConstants.ConfigureWebJsonOptions(options.SerializerOptions);

        // Pretty print the JSON in development for easier debugging.
        options.SerializerOptions.WriteIndented = environment.IsDevelopmentOrTest();
    }
}
