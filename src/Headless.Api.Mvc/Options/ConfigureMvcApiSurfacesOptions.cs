// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Mvc.Surfaces;
using Headless.Api.Surfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Headless.Api.Options;

internal sealed class ConfigureMvcApiSurfacesOptions(ApiSurfaceRegistry registry) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) => options.Conventions.Add(new ApiSurfaceConvention(registry));
}
