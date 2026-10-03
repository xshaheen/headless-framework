// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api.Surfaces;

/// <summary>Immutable document identity for a surface; API version groups remain independent.</summary>
[PublicAPI]
public sealed class ApiSurfaceOpenApiDescriptor(string documentName, string title)
{
    public string DocumentName { get; } = Argument.IsNotNullOrWhiteSpace(documentName);
    public string Title { get; } = Argument.IsNotNullOrWhiteSpace(title);
}
