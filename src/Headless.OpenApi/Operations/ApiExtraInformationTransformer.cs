// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Asp.Versioning;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Copies API Explorer facts the native generator leaves out: deprecation, and model-metadata parameter
/// descriptions.
/// </summary>
/// <remarks>
/// An operation is deprecated when its endpoint carries <see cref="ObsoleteAttribute"/>, or when Asp.Versioning
/// reports the described API version as deprecated. A parameter without a description takes the model-metadata
/// description (<c>[Display(Description = ...)]</c>). The native generator already marks a parameter required from the
/// same API Explorer flag, so this transformer leaves the parameter's required flag alone.
/// </remarks>
internal sealed class ApiExtraInformationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var description = context.Description;

        operation.Deprecated |= _IsDeprecated(description);

        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            var parameterDescription = description.ParameterDescriptions.FirstOrDefault(x =>
                string.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (parameterDescription is null)
            {
                continue;
            }

            parameter.Description ??= parameterDescription.ModelMetadata?.Description;
        }

        return Task.CompletedTask;
    }

    private static bool _IsDeprecated(ApiDescription description)
    {
        var metadata = description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<ObsoleteAttribute>().Any())
        {
            return true;
        }

        if (metadata.OfType<ApiVersionMetadata>().FirstOrDefault() is not { } versionMetadata)
        {
            return false;
        }

#pragma warning disable RCS1130 // False positive: Asp.Versioning combines its mapping values (1 and 2) as flags without the attribute.
        var model = versionMetadata.Map(ApiVersionMapping.Explicit | ApiVersionMapping.Implicit);
#pragma warning restore RCS1130

        // The versioned API Explorer records the version each description belongs to; without it, any deprecation counts.
        return description.Properties.TryGetValue(typeof(ApiVersion), out var value) && value is ApiVersion version
            ? model.DeprecatedApiVersions.Contains(version)
            : model.DeprecatedApiVersions.Count > 0;
    }
}
