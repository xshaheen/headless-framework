// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OData.Query;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.Api;

/// <summary>
/// NSwag operation processor that injects standard OData query-parameter documentation
/// (<c>$select</c>, <c>$expand</c>, <c>$filter</c>, <c>$search</c>, <c>$top</c>, <c>$skip</c>,
/// <c>$orderby</c>) into the generated OpenAPI operation.
/// </summary>
/// <remarks>
/// <para>
/// The processor activates for an operation when either of the following is true:
/// <list type="bullet">
///   <item><description>
///     The action method (or its declaring controller type) carries an <c>EnableQueryAttribute</c>.
///   </description></item>
///   <item><description>
///     The action has a parameter whose type is or derives from <c>ODataQueryOptions</c>. In that case
///     the <c>ODataQueryOptions</c> parameter is removed from the generated parameter list and replaced
///     by the individual OData query string parameters.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// See <see href="https://docs.microsoft.com/en-us/odata/concepts/queryoptions-overview"/> for OData
/// query option semantics.
/// </para>
/// </remarks>
public sealed class ODataOperationFilter : IOperationProcessor
{
    /// <summary>
    /// Processes the NSwag operation context and, when applicable, injects OData query parameters.
    /// </summary>
    /// <param name="context">The NSwag operation processor context for the current operation.</param>
    /// <returns>Always <see langword="true"/> so that subsequent processors continue to run.</returns>
    public bool Process(OperationProcessorContext context)
    {
        var operation = context.OperationDescription.Operation;

        var methodInfo = context.MethodInfo;

        if (methodInfo is null)
        {
            return true;
        }

        // Check for ODataQueryOptions parameter
        var odataQueryOptionsParameter = methodInfo
            .GetParameters()
            .SingleOrDefault(p => typeof(ODataQueryOptions).IsAssignableFrom(p.ParameterType));

        // Check for EnableQueryAttribute on method or declaring type
        var hasEnableQuery =
            methodInfo.GetCustomAttributes(inherit: true).Any(a => a is EnableQueryAttribute)
            || methodInfo.DeclaringType?.GetCustomAttributes(inherit: true).Any(a => a is EnableQueryAttribute) == true;

        if (hasEnableQuery || odataQueryOptionsParameter is not null)
        {
            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "$select",
                    Kind = OpenApiParameterKind.Query,
                    Schema = new NJsonSchema.JsonSchema { Type = NJsonSchema.JsonObjectType.String },
                    Description = "Returns only the selected properties. (ex. FirstName, LastName)",
                    IsRequired = false,
                }
            );

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "$expand",
                    Kind = OpenApiParameterKind.Query,
                    Schema = new NJsonSchema.JsonSchema { Type = NJsonSchema.JsonObjectType.String },
                    Description = "Include only the selected objects. (ex. Childrens, Locations)",
                    IsRequired = false,
                }
            );

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "$filter",
                    Kind = OpenApiParameterKind.Query,
                    Schema = new NJsonSchema.JsonSchema { Type = NJsonSchema.JsonObjectType.String },
                    Description = "Filter the response with OData filter queries.",
                    IsRequired = false,
                }
            );

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "$search",
                    Kind = OpenApiParameterKind.Query,
                    Schema = new NJsonSchema.JsonSchema { Type = NJsonSchema.JsonObjectType.String },
                    Description = "Filter the response with OData search queries.",
                    IsRequired = false,
                }
            );

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "$top",
                    Kind = OpenApiParameterKind.Query,
                    Schema = new NJsonSchema.JsonSchema { Type = NJsonSchema.JsonObjectType.Integer },
                    Description = "Number of objects to return. (ex. 25)",
                    IsRequired = false,
                }
            );

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "$skip",
                    Kind = OpenApiParameterKind.Query,
                    Schema = new NJsonSchema.JsonSchema { Type = NJsonSchema.JsonObjectType.Integer },
                    Description = "Number of objects to skip in the current order (ex. 50)",
                    IsRequired = false,
                }
            );

            operation.Parameters.Add(
                new OpenApiParameter
                {
                    Name = "$orderby",
                    Kind = OpenApiParameterKind.Query,
                    Schema = new NJsonSchema.JsonSchema { Type = NJsonSchema.JsonObjectType.String },
                    Description = "Define the order by one or more fields (ex. LastModified)",
                    IsRequired = false,
                }
            );
        }

        // Remove ODataQueryOptions parameter from operation if present
        if (odataQueryOptionsParameter is not null)
        {
            var toRemove = operation.Parameters.FirstOrDefault(p =>
                string.Equals(p.Name, odataQueryOptionsParameter.Name, StringComparison.Ordinal)
            );

            if (toRemove is not null)
            {
                operation.Parameters.Remove(toRemove);
            }
        }

        return true;
    }
}
