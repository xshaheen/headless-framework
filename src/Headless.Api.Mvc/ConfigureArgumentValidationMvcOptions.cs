// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

internal sealed class ConfigureArgumentValidationMvcOptions : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        // One instance per host: a type-registered filter is built again for every action invocation, and a static one
        // would keep every host's action descriptors alive for the process.
        if (!options.Filters.OfType<ArgumentValidationActionFilter>().Any())
        {
            options.Filters.Add(new ArgumentValidationActionFilter());
        }
    }
}
