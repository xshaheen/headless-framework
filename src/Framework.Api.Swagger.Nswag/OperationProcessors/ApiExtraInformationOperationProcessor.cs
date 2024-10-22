// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Framework.Api.Swagger.Nswag.OperationProcessors;

public sealed class ApiExtraInformationOperationProcessor : IOperationProcessor
{
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

        operation.IsDeprecated |= context.ApiDescription.IsDeprecated();

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
        foreach (var parameter in operation.Parameters)
        {
            var description = context.ApiDescription.ParameterDescriptions.FirstOrDefault(x =>
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
