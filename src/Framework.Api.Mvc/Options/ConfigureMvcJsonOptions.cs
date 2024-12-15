// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Framework.Api.Mvc.Options;

[PublicAPI]
public sealed class ConfigureMvcJsonOptions(IWebHostEnvironment environment) : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        FrameworkJsonConstants.ConfigureWebJsonOptions(options.JsonSerializerOptions);

        // Pretty print the JSON in development for easier debugging.
        options.JsonSerializerOptions.WriteIndented = environment.IsDevelopmentOrTest();
    }
}
