// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Constants;
using Headless.OpenApi.Nswag.OperationProcessors;
using NSwag;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.OpenApi.Nswag.Surfaces;

/// <summary>
/// Documents the missing-tenant 403 alongside the standard permission-denied 403, allowing API documentation
/// and generated SDKs to reflect both shapes permitted by <see cref="Models.ForbiddenProblemDetails"/>.
/// </summary>
public sealed class TenantRequiredForbiddenExampleOperationProcessor(bool includeTenantRequiredExample = true)
    : IOperationProcessor
{
    private const string _DefaultExampleName = "forbidden";
    private const string _TenantRequiredExampleName = "tenantRequired";
    private const string _TenantRequiredErrorCode = "g:tenant_required";

    /// <inheritdoc />
    public bool Process(OperationProcessorContext context)
    {
        Argument.IsNotNull(context);

        if (
            !context.OperationDescription.Operation.Responses.TryGetValue(
                OpenApiStatusCodes.Forbidden,
                out var forbidden
            )
        )
        {
            return true;
        }

        foreach (var mediaType in forbidden.Content.Values)
        {
            if (mediaType.Examples is not { } examples)
            {
                continue;
            }

            if (mediaType.Example is { } existing)
            {
                examples.TryAdd(
                    _DefaultExampleName,
                    new OpenApiExample { Summary = "Permission denied", Value = existing }
                );

                mediaType.Example = null;
            }

            if (includeTenantRequiredExample)
            {
                examples.TryAdd(
                    _TenantRequiredExampleName,
                    new OpenApiExample { Summary = "Tenant context required", Value = _CreateTenantRequiredExample() }
                );
            }
        }

        return true;
    }

    private static Dictionary<string, object?> _CreateTenantRequiredExample()
    {
        return new(StringComparer.Ordinal)
        {
            ["type"] = HeadlessProblemDetailsConstants.Types.Forbidden,
            ["title"] = HeadlessProblemDetailsConstants.Titles.Forbidden,
            ["status"] = 403,
            ["detail"] = HeadlessProblemDetailsConstants.Details.TenantContextRequired,
            ["instance"] = "/some-endpoint",
            ["error"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = _TenantRequiredErrorCode,
                ["description"] = HeadlessProblemDetailsConstants.Details.TenantContextRequired,
            },
            ["traceId"] = "<trace-id>",
            ["buildNumber"] = "<version>",
            ["commitNumber"] = "<commit>",
            ["timestamp"] = "2024-01-01T12:00:00+00:00",
        };
    }
}
