// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Options for the header tenant identifier source: the request header names read, in
/// order, for the raw tenant identifier. The default is the single <see cref="DefaultHeaderName"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every configured name shares one duplicate-detection scope: exactly one non-blank value across all
/// of them is the identifier, and any second value (a repeated line, or two names both present) is
/// ambiguous and rejects the request. A name listed more than once is read once. Each name is
/// also appended to the response <c>Vary</c> header on every consult.
/// </para>
/// <para>
/// Options contributions accumulate across <c>AddHeaderSource</c> calls, with one refinement so
/// the default is never silently kept beside an explicitly requested name: the string overload and a
/// configuration bind that lists names <em>replace</em> the list while it is still the untouched
/// default, and <em>append</em> once it has been customized. So <c>AddHeaderSource("X-Legacy")</c>
/// reads only <c>X-Legacy</c>, while <c>AddHeaderSource("X-Tenant").AddHeaderSource("X-Legacy")</c>
/// reads both. Names are validated at startup as HTTP tokens (RFC 9110 <c>tchar</c>).
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HeaderTenantIdentifierSourceOptions
{
    /// <summary>
    /// The default header name: <c>X-Tenant</c>. Deliberately not <c>X-Tenant-Id</c> — the value is a
    /// public identifier the catalog canonicalizes, never a tenant id.
    /// </summary>
    public const string DefaultHeaderName = "X-Tenant";

    // The list instance created by the constructor. Reference identity plus content identifies the
    // "untouched default" state that the replace-or-append contribution rule keys on.
    private readonly IList<string> _initialHeaderNames;

    /// <summary>Creates options reading the single <see cref="DefaultHeaderName"/>.</summary>
    public HeaderTenantIdentifierSourceOptions()
    {
        HeaderNames = _initialHeaderNames = [DefaultHeaderName];
    }

    /// <summary>
    /// The request header names read for the identifier, in order. Must contain at least one valid
    /// HTTP token (validated at startup); the source snapshots the distinct names (case-insensitively) once.
    /// </summary>
    public IList<string> HeaderNames { get; set; }

    /// <summary>
    /// <see langword="true"/> while <see cref="HeaderNames"/> is still the constructor's list holding
    /// only <see cref="DefaultHeaderName"/> — nothing has replaced, cleared, or added to it.
    /// </summary>
    internal bool HasUntouchedDefaultHeaderNames =>
        ReferenceEquals(HeaderNames, _initialHeaderNames)
        && HeaderNames.Count == 1
        && string.Equals(HeaderNames[0], DefaultHeaderName, StringComparison.Ordinal);

    /// <summary>
    /// Contributes one header name: replaces the untouched default list, otherwise
    /// appends — unless the name is already listed (case-insensitively), which is a no-op.
    /// </summary>
    internal void ContributeHeaderName(string headerName)
    {
        if (HasUntouchedDefaultHeaderNames)
        {
            HeaderNames = [headerName];
        }
        else if (!HeaderNames.Contains(headerName, StringComparer.OrdinalIgnoreCase))
        {
            // Header names are case-insensitive on the wire, so a repeat contribution is the same
            // intent restated, not a second header to read.
            HeaderNames.Add(headerName);
        }
    }
}

/// <summary>Validator for <see cref="HeaderTenantIdentifierSourceOptions"/>.</summary>
internal sealed class HeaderTenantIdentifierSourceOptionsValidator
    : AbstractValidator<HeaderTenantIdentifierSourceOptions>
{
    public HeaderTenantIdentifierSourceOptionsValidator()
    {
        RuleFor(x => x.HeaderNames).NotEmpty();

        RuleForEach(x => x.HeaderNames)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A tenant header name must not be blank.")
            .Must(_IsHttpToken)
            .WithMessage(
                (_, headerName) =>
                    $"The tenant header name '{headerName}' is not a valid HTTP token: only letters, digits, "
                    + "and !#$%&'*+-.^_`|~ are allowed (no whitespace or separators)."
            );
    }

    /// <summary>
    /// RFC 9110 <c>token</c>: one or more <c>tchar</c>, where <c>tchar</c> is a letter, a digit, or one
    /// of <c>!#$%&amp;'*+-.^_`|~</c>. A char loop rather than a regex: the alphabet is fixed and there
    /// is no pattern to time out.
    /// </summary>
    private static bool _IsHttpToken(string headerName)
    {
        const string tcharPunctuation = "!#$%&'*+-.^_`|~";

        foreach (var c in headerName)
        {
            if (!char.IsAsciiLetterOrDigit(c) && !tcharPunctuation.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
