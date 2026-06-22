// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Options that control which Headless-specific features are applied during NSwag document generation.
/// </summary>
/// <remarks>
/// Pass an <c>Action&lt;HeadlessNswagOptions&gt;</c> to <c>AddNswagOpenApi</c> to override defaults.
/// Security schemes (<see cref="AddBearerSecurity"/>, <see cref="AddApiKeySecurity"/>) are applied
/// after any caller-supplied generator settings so they always appear in the final document.
/// </remarks>
public sealed class HeadlessNswagOptions
{
    /// <summary>
    /// Whether to register an HTTP Bearer / JWT security scheme and apply it to all operations
    /// that require authentication. Default is <see langword="true"/>.
    /// </summary>
    public bool AddBearerSecurity { get; set; } = true;

    /// <summary>
    /// Whether to register an API key security scheme that reads from a request header.
    /// Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The header name is controlled by <see cref="ApiKeyHeaderName"/>.
    /// </remarks>
    public bool AddApiKeySecurity { get; set; }

    /// <summary>
    /// The HTTP header name used for API key authentication when <see cref="AddApiKeySecurity"/> is
    /// <see langword="true"/>. Default is <c>X-API-Key</c>.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    /// <summary>
    /// Whether to register NJsonSchema type mappers for the built-in Headless primitive types
    /// (<c>Money</c>, <c>Month</c>, <c>AccountId</c>, <c>UserId</c>). Default is <see langword="true"/>.
    /// </summary>
    public bool AddPrimitiveMappings { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, schema-processor and FluentValidation-rule errors are re-thrown
    /// instead of being logged and swallowed. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Enable this during development to surface validator configuration problems early. In production
    /// the default swallow-and-log behavior prevents a single misconfigured validator from breaking
    /// the entire OpenAPI document.
    /// </remarks>
    public bool ThrowOnSchemaProcessingError { get; set; }
}
