// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Framework.Api.Swagger.Swashbuckle.OperationFilters;

/// <summary>
/// An Open API operation filter used to document the implicit API version parameter.
/// </summary>
/// <remarks>This <see cref="IOperationFilter"/> is only required due to bugs in the <see cref="SwaggerGenerator"/>.
/// Once they are fixed and published, this class can be removed. See:
/// - https://github.com/domaindrivendev/Swashbuckle.AspNetCore/issues/412
/// - https://github.com/domaindrivendev/Swashbuckle.AspNetCore/pull/413</remarks>
public sealed class ApiVersionOperationFilter : IOperationFilter
{
    /// <inheritdoc/>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        Argument.IsNotNull(operation);
        Argument.IsNotNull(context);

        operation.Deprecated |= context.ApiDescription.IsDeprecated();

        // Add supported response types like text/json, application/json, ... fore each request
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

        if (operation.Parameters is null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            var description = context.ApiDescription.ParameterDescriptions.First(x =>
                string.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
            );

            parameter.Description ??= description.ModelMetadata.Description;

            if (parameter.Schema.Default is null && description.DefaultValue is not null)
            {
                var json = JsonSerializer.Serialize(description.DefaultValue, description.ModelMetadata.ModelType);

                parameter.Schema.Default = OpenApiAnyFactory.CreateFromJson(json);
            }

            parameter.Required |= description.IsRequired;
        }
    }
}
