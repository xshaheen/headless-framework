// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.Api.OperationProcessors;

/// <summary>
/// Converts query parameter names to camelCase in the generated OpenAPI specification.
/// Parameters prefixed with <c>$</c> (e.g., OData parameters like <c>$filter</c>) are left unchanged.
/// </summary>
public sealed class CamelCaseQueryParameterOperationProcessor : IOperationProcessor
{
    /// <summary>
    /// Applies camelCase conversion to all query parameter names in the operation.
    /// </summary>
    /// <param name="context">The NSwag operation processor context for the current operation.</param>
    /// <returns>Always <see langword="true"/> so that subsequent processors continue to run.</returns>
    public bool Process(OperationProcessorContext context)
    {
        var parameters = context.OperationDescription.Operation.Parameters;

        if (parameters is null)
        {
            return true;
        }

        foreach (var parameter in parameters)
        {
            if (
                parameter.Kind == OpenApiParameterKind.Query
                && !string.IsNullOrEmpty(parameter.Name)
                && !parameter.Name.StartsWith('$')
            )
            {
                parameter.Name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name);
            }
        }

        return true;
    }
}
