// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
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
