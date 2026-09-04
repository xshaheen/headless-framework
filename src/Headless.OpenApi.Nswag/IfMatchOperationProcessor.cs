// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.OpenApi.Nswag.OperationProcessors;
using NJsonSchema;
using NSwag;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.OpenApi.Nswag;

/// <summary>Adds the required <c>If-Match</c> header and 428 response to annotated operations.</summary>
public sealed class IfMatchOperationProcessor : IOperationProcessor
{
    /// <inheritdoc/>
    public bool Process(OperationProcessorContext context)
    {
        var requiresIfMatch =
            context.MethodInfo?.IsDefined(typeof(RequireIfMatchAttribute), inherit: true) == true
            || context.ControllerType?.IsDefined(typeof(RequireIfMatchAttribute), inherit: true) == true
            || (
                context is AspNetCoreOperationProcessorContext aspNetCoreContext
                && aspNetCoreContext
                    .ApiDescription.ActionDescriptor.EndpointMetadata.OfType<RequireIfMatchAttribute>()
                    .Any()
            );
        if (!requiresIfMatch)
        {
            return true;
        }

        context.OperationDescription.Operation.Parameters.Add(
            new OpenApiParameter
            {
                Name = "If-Match",
                Kind = OpenApiParameterKind.Header,
                IsRequired = true,
                Description = "One strong entity tag returned by the resource.",
                Schema = new JsonSchema { Type = JsonObjectType.String },
            }
        );
        context.OperationDescription.Operation.Responses.TryAdd(
            OpenApiStatusCodes.BadRequest,
            new OpenApiResponse { Description = "The supplied If-Match value was invalid." }
        );
        context.OperationDescription.Operation.Responses.TryAdd(
            OpenApiStatusCodes.PreconditionRequired,
            new OpenApiResponse { Description = "The required If-Match precondition was not supplied." }
        );

        return true;
    }
}
