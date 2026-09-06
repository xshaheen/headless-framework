// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Concurrency;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

internal sealed class ConfigureEntityTagConcurrencyMvcOptions : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        if (
            !options.Filters.OfType<TypeFilterAttribute>().Any(x => x.ImplementationType == typeof(IfMatchActionFilter))
        )
        {
            options.Filters.Add<IfMatchActionFilter>();
        }

        if (
            !options
                .Filters.OfType<TypeFilterAttribute>()
                .Any(x => x.ImplementationType == typeof(EntityTagResponseFilter))
        )
        {
            options.Filters.Add<EntityTagResponseFilter>();
        }
    }
}
