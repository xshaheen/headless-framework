// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Options for the Headless enrichment that <see cref="SetupHeadlessOpenApi.AddHeadlessTransformers"/> applies to
/// every OpenAPI document: security schemes, the per-operation security requirements, and FluentValidation schema
/// constraints.
/// </summary>
/// <remarks>
/// One instance is shared by every Headless document in the application. Configure it with
/// <c>ConfigureHeadlessOpenApi(...)</c>; registration order relative to the document registrations does not matter.
/// Besides the schemes configured here, every document publishes one scheme per credential of each registered
/// authentication scheme whose options implement <see cref="Headless.Api.IHttpCredentialSchemeOptions"/>, such as the
/// API-key and Basic schemes of <c>Headless.Api.Identity</c>. A scheme configured here wins over a discovered scheme
/// with the same name.
/// </remarks>
[PublicAPI]
public sealed class HeadlessOpenApiOptions
{
    /// <summary>The component name of the built-in HTTP bearer (JWT) security scheme.</summary>
    public const string BearerSchemeName = "Bearer";

    /// <summary>The component name of the built-in API-key security scheme.</summary>
    public const string ApiKeySchemeName = "ApiKey";

    /// <summary>
    /// Whether to register an HTTP bearer (JWT) security scheme named <see cref="BearerSchemeName"/>.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool AddBearerSecurity { get; set; } = true;

    /// <summary>
    /// Whether every operation documents the 404, 409, and 422 problem responses. Default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// These are the statuses the exception handler and the <c>ApiResult</c> conversion write for not-found,
    /// conflict, and validation failures. Any handler or service can raise them, so endpoint metadata cannot tell
    /// which operations return them; the document lists them on every operation instead.
    /// </remarks>
    public bool StandardProblemResponses { get; set; } = true;

    /// <summary>
    /// Whether every operation documents the <c>4XX</c> and <c>5XX</c> range responses typed with
    /// <c>HeadlessProblemDetails</c>. Default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The ranges let a generated client read a status no rule predicts (a timeout, an unavailable dependency, an
    /// application's mapped exception) as a typed problem. Turn them off API-wide when the document feeds a client
    /// generator that mishandles range keys: NSwag's C# generator emits code that does not compile for them, and
    /// openapi-generator's <c>typescript-fetch</c> ignores them. The endpoint markers and the surface setting do not
    /// remove the ranges; only this option does.
    /// </remarks>
    public bool ProblemResponseRanges { get; set; } = true;

    /// <summary>
    /// Whether to register an API-key security scheme named <see cref="ApiKeySchemeName"/> that reads the
    /// <see cref="ApiKeyHeaderName"/> header. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Leave it off when the API key is checked by the <c>Headless.Api.Identity</c> handler: its scheme is published
    /// from the handler's own options, query fallback included. Turn it on for an API key checked elsewhere.
    /// </remarks>
    public bool AddApiKeySecurity { get; set; }

    /// <summary>
    /// The request header carrying the API key when <see cref="AddApiKeySecurity"/> is <see langword="true"/>.
    /// Default is <c>X-Api-Key</c>, the header the <c>Headless.Api.Identity</c> API-key handler reads by default.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = HttpHeaderNames.ApiKey;

    /// <summary>
    /// Additional security schemes to publish under <c>components.securitySchemes</c>, keyed by component name.
    /// They sit beside the built-in schemes enabled by <see cref="AddBearerSecurity"/> and
    /// <see cref="AddApiKeySecurity"/>.
    /// </summary>
    public IDictionary<string, OpenApiSecurityScheme> SecuritySchemes { get; } =
        new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal);

    /// <summary>
    /// Chooses the security schemes an operation that requires authentication accepts, by component name. Each
    /// returned name becomes a separate security requirement, so a client may satisfy any one of them.
    /// </summary>
    /// <remarks>
    /// The selector runs only for operations whose endpoint metadata requires an authenticated user and does not
    /// allow anonymous access. When it is <see langword="null"/>, every registered scheme is offered. Returning a
    /// name that no registered scheme carries fails document generation, because the requirement would reference
    /// a missing component.
    /// </remarks>
    public Func<ApiDescription, IEnumerable<string>>? SecuritySchemeSelector { get; set; }

    /// <summary>
    /// FluentValidation rules that add to or replace <see cref="FluentValidationRule.DefaultRules"/>. A rule
    /// whose <see cref="FluentValidationRule.RuleName"/> matches a default replaces that default.
    /// </summary>
    public IList<FluentValidationRule> FluentValidationRules { get; } = [];

    /// <summary>
    /// When <see langword="true"/>, an error raised while a FluentValidation rule shapes a schema fails document
    /// generation instead of being logged and skipped. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Enable it in development to surface validator configuration mistakes. The default keeps one misconfigured
    /// validator from taking the whole document offline.
    /// </remarks>
    public bool ThrowOnSchemaProcessingError { get; set; }

    // Built on first use, once the options are configured and validated; the options instance is not mutated after that.
    private IReadOnlyDictionary<string, OpenApiSecurityScheme>? _securitySchemes;
    private FluentValidationRule[]? _fluentValidationRules;

    /// <summary>The built-in and additional security schemes, by component name.</summary>
    internal IReadOnlyDictionary<string, OpenApiSecurityScheme> GetSecuritySchemes()
    {
        return _securitySchemes ??= _BuildSecuritySchemes();
    }

    /// <summary><see cref="FluentValidationRule.DefaultRules"/> with <see cref="FluentValidationRules"/> merged in by name.</summary>
    internal FluentValidationRule[] GetFluentValidationRules()
    {
        return _fluentValidationRules ??= _MergeFluentValidationRules();
    }

    private FluentValidationRule[] _MergeFluentValidationRules()
    {
        if (FluentValidationRules.Count == 0)
        {
            return FluentValidationRule.DefaultRules;
        }

        var map = FluentValidationRule.DefaultRules.ToDictionary(
            rule => rule.RuleName,
            rule => rule,
            StringComparer.Ordinal
        );

        foreach (var rule in FluentValidationRules)
        {
            map[rule.RuleName] = rule;
        }

        return [.. map.Values];
    }

    private Dictionary<string, OpenApiSecurityScheme> _BuildSecuritySchemes()
    {
        var schemes = new Dictionary<string, OpenApiSecurityScheme>(StringComparer.Ordinal);

        if (AddBearerSecurity)
        {
            schemes[BearerSchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = """
                    JWT Authorization header using the Bearer scheme.
                    Enter Token without any prefix.
                    """,
            };
        }

        if (AddApiKeySecurity)
        {
            schemes[ApiKeySchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = ApiKeyHeaderName,
                In = ParameterLocation.Header,
                Description = $"API Key authentication using the {ApiKeyHeaderName} header.",
            };
        }

        foreach (var (name, scheme) in SecuritySchemes)
        {
            schemes[name] = scheme;
        }

        return schemes;
    }
}

internal sealed class HeadlessOpenApiOptionsValidator : AbstractValidator<HeadlessOpenApiOptions>
{
    public HeadlessOpenApiOptionsValidator()
    {
        RuleFor(x => x.ApiKeyHeaderName).NotEmpty().When(x => x.AddApiKeySecurity);

        RuleForEach(x => x.SecuritySchemes)
            .Must(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .WithMessage("Security scheme names must not be empty.")
            .Must(entry => entry.Value is not null)
            .WithMessage("Security scheme '{PropertyValue}' must not be null.");

        RuleFor(x => x.SecuritySchemes)
            .Must(schemes => !schemes.ContainsKey(HeadlessOpenApiOptions.BearerSchemeName))
            .When(x => x.AddBearerSecurity)
            .WithMessage(
                $"'{HeadlessOpenApiOptions.BearerSchemeName}' is the built-in bearer scheme. Set AddBearerSecurity to false to replace it."
            );

        RuleFor(x => x.SecuritySchemes)
            .Must(schemes => !schemes.ContainsKey(HeadlessOpenApiOptions.ApiKeySchemeName))
            .When(x => x.AddApiKeySecurity)
            .WithMessage(
                $"'{HeadlessOpenApiOptions.ApiKeySchemeName}' is the built-in API-key scheme. Set AddApiKeySecurity to false to replace it."
            );

        RuleForEach(x => x.FluentValidationRules).NotNull();
    }
}
