// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Options for the cross-layer "missing tenant context" 400 ProblemDetails response produced by
/// <c>TenantContextExceptionHandler</c> and <c>IProblemDetailsCreator.TenantRequired</c>.
/// </summary>
/// <remarks>
/// The response Title is fixed at <c>HeadlessProblemDetailsConstants.Titles.TenantContextRequired</c>
/// to match the framework's other ProblemDetails (every shipped factory uses a single canonical title).
/// Entity names, exception messages, and <see cref="System.Exception.Data"/> tags are deliberately
/// not surfaced in the response — they belong in server logs, not HTTP bodies.
/// </remarks>
[PublicAPI]
public sealed class TenantContextProblemDetailsOptions
{
    /// <summary>
    /// Consumer-controlled URI namespace for the response's <c>type</c> field. The final URL is
    /// <c>{TypeUriPrefix}/tenant-required</c>; any trailing slash is trimmed so the joined URL has
    /// a single separator. Default: <c>https://errors.headless/tenancy</c>.
    /// </summary>
    public string TypeUriPrefix { get; init; } = "https://errors.headless/tenancy";

    /// <summary>
    /// Stable client-routing identifier written to <c>Extensions["code"]</c>. Default:
    /// <c>tenancy.tenant-required</c>.
    /// </summary>
    public string ErrorCode { get; init; } = "tenancy.tenant-required";
}

internal sealed class TenantContextProblemDetailsOptionsValidator
    : AbstractValidator<TenantContextProblemDetailsOptions>
{
    public TenantContextProblemDetailsOptionsValidator()
    {
        RuleFor(x => x.TypeUriPrefix)
            .NotEmpty()
            .Must(value => Uri.TryCreate(value, UriKind.Absolute, out _))
            .WithMessage("'{PropertyName}' must be a well-formed absolute URI.");

        RuleFor(x => x.ErrorCode).NotEmpty();
    }
}
