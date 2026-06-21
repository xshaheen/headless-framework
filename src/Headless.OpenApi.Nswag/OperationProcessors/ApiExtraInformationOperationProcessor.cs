// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc.ApiExplorer;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.Api.OperationProcessors;

/// <summary>
/// NSwag operation processor that enriches generated operations with supplemental API-Explorer metadata:
/// deprecated status, supported response content types, and parameter descriptions and default values.
/// </summary>
/// <remarks>
/// <para>
/// Only processes <c>AspNetCoreOperationProcessorContext</c> instances; other context types are passed
/// through unchanged.
/// </para>
/// <para>
/// Three enrichments are applied per operation:
/// <list type="number">
///   <item><description>
///     Sets <c>IsDeprecated = true</c> when the underlying <c>ApiDescription</c> is marked deprecated.
///   </description></item>
///   <item><description>
///     Removes response content-type entries from each status-code response that are not listed in the
///     API explorer's <c>SupportedResponseTypes</c>, keeping only the negotiated media types.
///   </description></item>
///   <item><description>
///     For each operation parameter, fills in <c>Description</c> from model metadata and
///     <c>Schema.Default</c> from the API explorer's default value when not already set.
///     Also sets <c>IsRequired = true</c> when the parameter descriptor marks it as required.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ApiExtraInformationOperationProcessor : IOperationProcessor
{
    /// <summary>
    /// Enriches the operation with deprecated status, response content types, and parameter metadata.
    /// </summary>
    /// <param name="context">The NSwag operation processor context for the current operation.</param>
    /// <returns>Always <see langword="true"/> so that subsequent processors continue to run.</returns>
    public bool Process(OperationProcessorContext context)
    {
        if (context is not AspNetCoreOperationProcessorContext ctx)
        {
            return true;
        }

        _Process(ctx);

        return true;
    }

    private static void _Process(AspNetCoreOperationProcessorContext context)
    {
        var operation = context.OperationDescription.Operation;

        operation.IsDeprecated |= context.ApiDescription.IsDeprecated;

        // Add supported response types like text/json, application/json, ...
        _AddSupportedResponseTypes(context, operation);

        // Add description and default value for each parameter
        if (operation.Parameters is not null)
        {
            _AddParametersDescriptions(context, operation);
        }
    }

    private static void _AddSupportedResponseTypes(
        AspNetCoreOperationProcessorContext context,
        OpenApiOperation operation
    )
    {
        foreach (var responseType in context.ApiDescription.SupportedResponseTypes)
        {
            var responseKey = responseType.IsDefaultResponse
                ? "default"
                : responseType.StatusCode.ToString(CultureInfo.InvariantCulture);

            var response = operation.Responses[responseKey];

            foreach (var contentType in response.Content.Keys)
            {
                if (
                    responseType.ApiResponseFormats.All(x =>
                        !string.Equals(x.MediaType, contentType, StringComparison.Ordinal)
                    )
                )
                {
                    response.Content.Remove(contentType);
                }
            }
        }
    }

    private static void _AddParametersDescriptions(
        AspNetCoreOperationProcessorContext context,
        OpenApiOperation operation
    )
    {
        foreach (var parameter in operation.Parameters ?? [])
        {
            var description = context.ApiDescription?.ParameterDescriptions?.FirstOrDefault(x =>
                string.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (description is null)
            {
                continue;
            }

            parameter.Description ??= description.ModelMetadata.Description;

            if (parameter.Schema.Default is null && description.DefaultValue is not null)
            {
                parameter.Schema.Default =
                    description.DefaultValue is string
                        ? description.DefaultValue
                        : JsonSerializer.Serialize(description.DefaultValue, description.ModelMetadata.ModelType);
            }

            parameter.IsRequired |= description.IsRequired;
        }
    }
}
