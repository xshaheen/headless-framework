// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Mvc.Surfaces;
using Headless.Api.Surfaces;
using Headless.Checks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

internal sealed class ConfigureMvcApiSurfacesOptions(IOptions<ApiSurfaceOptions> surfaceOptions)
    : IConfigureOptions<MvcOptions>
{
    private readonly IOptions<ApiSurfaceOptions> _surfaceOptions = Argument.IsNotNull(surfaceOptions);

    public void Configure(MvcOptions options)
    {
        options.Conventions.Add(new ApiSurfaceConvention(_surfaceOptions));
    }
}
