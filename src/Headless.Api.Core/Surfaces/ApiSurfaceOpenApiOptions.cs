// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api.Surfaces;

/// <summary>Configures document identity independently of ApiExplorer version groups.</summary>
[PublicAPI]
public sealed class ApiSurfaceOpenApiOptions(string surfaceName)
{
    public string DocumentName { get; set; } = surfaceName.ToLowerInvariant();
    public string Title { get; set; } = $"{surfaceName} API";
}
