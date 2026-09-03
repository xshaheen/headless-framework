// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Concurrency;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

internal sealed class ConfigureEtagConcurrencyMvcOptions : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        if (
            !options.Filters.OfType<TypeFilterAttribute>().Any(x => x.ImplementationType == typeof(IfMatchActionFilter))
        )
        {
            options.Filters.Add<IfMatchActionFilter>();
        }

        if (!options.Filters.OfType<TypeFilterAttribute>().Any(x => x.ImplementationType == typeof(EtagResponseFilter)))
        {
            options.Filters.Add<EtagResponseFilter>();
        }
    }
}
