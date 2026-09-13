// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Surfaces;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Headless.OpenApi.Nswag.Surfaces;

internal sealed class ApiSurfaceOperationProcessor(string surfaceName) : IOperationProcessor
{
    public bool Process(OperationProcessorContext context) =>
        context is AspNetCoreOperationProcessorContext aspNet
        && string.Equals(
            aspNet
                .ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IApiSurfaceMetadata>()
                .LastOrDefault()
                ?.SurfaceName,
            surfaceName,
            StringComparison.OrdinalIgnoreCase
        );
}
