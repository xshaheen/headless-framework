// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Api;

public sealed class HeadlessNswagOptions
{
    public bool AddBearerSecurity { get; set; } = true;

    public bool AddApiKeySecurity { get; set; }

    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    public bool AddPrimitiveMappings { get; set; } = true;

    /// <summary>
    /// When true, throws exceptions during schema processing instead of logging and continuing.
    /// Useful for development to catch validation configuration issues early.
    /// Default is false to maintain backward compatibility.
    /// </summary>
    public bool ThrowOnSchemaProcessingError { get; set; }
}
