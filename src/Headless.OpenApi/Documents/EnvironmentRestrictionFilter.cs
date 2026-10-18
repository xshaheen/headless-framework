// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.OpenApi;

/// <summary>
/// Leaves out of every Headless document the endpoints whose <see cref="IEnvironmentRestrictionMetadata"/> says they
/// answer 404 in the current host environment.
/// </summary>
/// <remarks>
/// It wraps <see cref="OpenApiOptions.ShouldInclude"/> after every configure callback ran, so it composes with the
/// document's own predicate and drops the endpoint before the generator creates its path or schemas. It applies only
/// to documents that carry the Headless transformers.
/// </remarks>
internal sealed class EnvironmentRestrictionFilter(IServiceProvider services) : IPostConfigureOptions<OpenApiOptions>
{
    public void PostConfigure(string? name, OpenApiOptions options)
    {
        // A container without a host has no environment to compare with, so nothing is left out.
        if (
            !SetupHeadlessOpenApi.HasHeadlessTransformers(options)
            || services.GetService<IHostEnvironment>() is not { } environment
        )
        {
            return;
        }

        var include = options.ShouldInclude;
        options.ShouldInclude = description => _IsAvailable(description, environment) && include(description);
    }

    private static bool _IsAvailable(ApiDescription description, IHostEnvironment environment)
    {
        return description
            .ActionDescriptor.EndpointMetadata.OfType<IEnvironmentRestrictionMetadata>()
            .All(restriction => restriction.IsAvailableIn(environment));
    }
}
