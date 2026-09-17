// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Options for the host tenant identifier source (R1, R7): the host templates matched against
/// <c>Request.Host.Host</c>, in order — the first template that matches yields its <c>{tenant}</c>
/// capture as the raw identifier.
/// </summary>
/// <remarks>
/// Templates follow the grammar owned by <c>HostTemplate</c> (KTD2): <c>{tenant}</c> captures
/// exactly one DNS-style label, <c>?</c> matches one label, <c>*</c> matches zero or more labels,
/// and every other label is a literal. A bare <c>{tenant}</c> matches the whole host and requires
/// the catalog-wide <c>IdentifierPattern</c>/<c>MaxIdentifierLength</c> override described in the
/// docs (R9). Options contributions accumulate across <c>AddHostSource</c> calls, so the string
/// convenience overload appends to this list rather than replacing it (KTD3).
/// </remarks>
[PublicAPI]
public sealed class HostTenantIdentifierSourceOptions
{
    /// <summary>
    /// The host templates to match, in priority order. Must contain at least one parseable template
    /// (validated at startup). Every entry is compiled once by the source.
    /// </summary>
    public IList<string> Templates { get; set; } = [];
}

/// <summary>Validator for <see cref="HostTenantIdentifierSourceOptions"/> (R7).</summary>
internal sealed class HostTenantIdentifierSourceOptionsValidator : AbstractValidator<HostTenantIdentifierSourceOptions>
{
    public HostTenantIdentifierSourceOptionsValidator()
    {
        RuleFor(x => x.Templates).NotEmpty();

        // The parser's message names the offending template and the broken rule; surfacing it
        // verbatim gives the operator the exact fix (KTD2, R7).
        RuleFor(x => x.Templates)
            .Must(_AllTemplatesParse)
            .WithMessage(x =>
                string.Join(
                    "; ",
                    x.Templates.Where(t => !HostTemplate.TryParse(t, out _, out _)).Select(HostTemplate.Validate)
                )
            );
    }

    private static bool _AllTemplatesParse(IList<string> templates)
    {
        return templates.All(template => HostTemplate.TryParse(template, out _, out _));
    }
}
