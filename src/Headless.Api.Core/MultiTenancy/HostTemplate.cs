// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Headless.Checks;
using Headless.Constants;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// A parsed and compiled host template: dot-separated labels where <c>{tenant}</c> names the
/// capture of exactly one label, <c>?</c> matches exactly one label, <c>*</c> matches zero or more
/// labels, and any other label is a literal (letters, digits, hyphen). A bare <c>{tenant}</c>
/// template matches the whole host.
/// </summary>
/// <remarks>
/// <para>
/// Parsing and compilation happen once: startup options validation proves every template parses,
/// and <see cref="HostTenantIdentifierSource"/> compiles its options in its constructor — so
/// <see cref="Match"/> never parses per request.
/// </para>
/// <para>
/// The compiled form is an anchored regex carrying <see cref="RegexPatterns.MatchTimeout"/> and
/// <see cref="RegexOptions.NonBacktracking"/>: the grammar has no backreferences or lookarounds, so
/// matching is linear in the host length and the timeout is defense in depth, practically
/// unreachable — the source still catches <see cref="RegexMatchTimeoutException"/>.
/// </para>
/// </remarks>
internal sealed class HostTemplate
{
    private const string _TenantToken = "{tenant}";

    // NonBacktracking cannot be combined with Compiled; the grammar is backtracking-free, so the
    // automaton engine is a strict improvement over a compiled backtracking regex here.
    // CultureInvariant: hostnames are ASCII case-insensitive (RFC 4343), so the process culture must
    // not change which literal labels match — under tr-TR, IgnoreCase alone folds 'I'/'i' through the
    // Turkish dotted/dotless forms and a literal label containing 'i' stops matching its uppercase form.
    private const RegexOptions _MatchOptions =
        RegexOptions.NonBacktracking
        | RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.ExplicitCapture;

    private readonly Regex _regex;

    private HostTemplate(Regex regex)
    {
        _regex = regex;
    }

    /// <summary>
    /// Parses and compiles a host template, reporting a human-readable message the options validator
    /// surfaces verbatim — every message names the offending template and the broken rule.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="hostTemplate">The compiled template, or <see langword="null"/> on failure.</param>
    /// <param name="error">The failure message, or <see langword="null"/> on success.</param>
    /// <param name="matchTimeout">
    /// Overrides <see cref="RegexPatterns.MatchTimeout"/> on the compiled regex. Test seam for forcing
    /// <see cref="RegexMatchTimeoutException"/>; production callers take the default.
    /// </param>
    /// <returns><see langword="true"/> when the template parsed; <see langword="false"/> otherwise.</returns>
    public static bool TryParse(
        string? template,
        out HostTemplate? hostTemplate,
        out string? error,
        TimeSpan? matchTimeout = null
    )
    {
        hostTemplate = null;

        if (string.IsNullOrWhiteSpace(template))
        {
            error = "Invalid host template: the template must not be null, empty, or whitespace.";
            return false;
        }

        if (template.Contains(':', StringComparison.Ordinal))
        {
            error =
                $"Invalid host template '{template}': ports are not allowed — the host source matches "
                + "the host without its port.";
            return false;
        }

        var labels = template.Split('.');

        foreach (var label in labels)
        {
            if (label.Length == 0)
            {
                error =
                    $"Invalid host template '{template}': empty labels are not allowed — remove "
                    + "leading/trailing dots and '..' sequences.";
                return false;
            }

            if (label is _TenantToken or "?" or "*")
            {
                continue;
            }

            if (
                label.Contains('{', StringComparison.Ordinal)
                || label.Contains('}', StringComparison.Ordinal)
                || label.Contains('?', StringComparison.Ordinal)
                || label.Contains('*', StringComparison.Ordinal)
            )
            {
                error =
                    $"Invalid host template '{template}': '?', '*', and '{{tenant}}' must each occupy "
                    + $"exactly one dot-separated label — '{label}' mixes a token with other text.";
                return false;
            }

            if (!label.All(c => char.IsLetterOrDigit(c) || c == '-'))
            {
                error =
                    $"Invalid host template '{template}': the label '{label}' may contain only "
                    + "letters, digits, and hyphens.";
                return false;
            }
        }

        var tokenCount = labels.Count(label => string.Equals(label, _TenantToken, StringComparison.Ordinal));

        if (tokenCount != 1)
        {
            error =
                $"Invalid host template '{template}': it must contain exactly one {{tenant}} token "
                + $"(found {tokenCount.ToString(CultureInfo.InvariantCulture)}).";
            return false;
        }

        hostTemplate = new HostTemplate(
            new Regex(_CompilePattern(labels), _MatchOptions, matchTimeout ?? RegexPatterns.MatchTimeout)
        );
        error = null;
        return true;
    }

    /// <summary>Returns the parser's failure message for <paramref name="template"/>, or <see langword="null"/> when it parses.</summary>
    public static string? Validate(string? template)
    {
        return TryParse(template, out _, out var error) ? null : error;
    }

    /// <summary>
    /// Matches an exact host (case-insensitive) and extracts the <c>{tenant}</c> capture.
    /// A <see cref="RegexMatchTimeoutException"/> propagates to the caller — the host source owns
    /// mapping it to an invalid result plus the once-per-process warning.
    /// </summary>
    /// <param name="host">The host without its port. Trailing-dot stripping is also the caller's job.</param>
    /// <param name="identifier">The captured identifier, or <see langword="null"/> on no match.</param>
    /// <returns><see langword="true"/> when the host matched; <see langword="false"/> otherwise.</returns>
    public bool Match(string host, out string? identifier)
    {
        Argument.IsNotNull(host);

        var match = _regex.Match(host);

        if (match.Success)
        {
            identifier = match.Groups["tenant"].Value;
            return true;
        }

        identifier = null;
        return false;
    }

    // Label fragments: "{tenant}" -> "(?<tenant>[^.]+)", "?" -> "[^.]+", literal -> escaped text.
    // A '*' that is not the last label compiles to "(?:[^.]+\.)*" (zero or more "label." prefixes,
    // carrying its own trailing separator); a trailing '*' compiles to "(?:\.[^.]+)*" (zero or more
    // ".label" suffixes, carrying its own leading separator) — mirroring Finbuckle's HostStrategy
    // shape so "{tenant}.*" matches the bare "acme" and "*.{tenant}" matches "a.b.acme".
    // A bare "{tenant}" is the whole-host (custom-domain) form: it captures the entire host,
    // dots included — the catalog's IdentifierPattern/MaxIdentifierLength override governs that
    // shape downstream, not the template grammar.
    private static string _CompilePattern(string[] labels)
    {
        if (labels is [_TenantToken])
        {
            return "^(?<tenant>.+)$";
        }

        var builder = new StringBuilder("^");
        var skipNextSeparator = false;

        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            var isFirst = i == 0;
            var isLast = i == labels.Length - 1;

            if (!isFirst && !skipNextSeparator && !(isLast && string.Equals(label, "*", StringComparison.Ordinal)))
            {
                builder.Append("\\.");
            }

            switch (label)
            {
                case _TenantToken:
                    builder.Append("(?<tenant>[^.]+)");
                    skipNextSeparator = false;
                    break;

                case "?":
                    builder.Append("[^.]+");
                    skipNextSeparator = false;
                    break;

                case "*" when isLast:
                    builder.Append("(?:\\.[^.]+)*");
                    skipNextSeparator = false;
                    break;

                case "*":
                    builder.Append("(?:[^.]+\\.)*");
                    skipNextSeparator = true;
                    break;

                default:
                    builder.Append(Regex.Escape(label));
                    skipNextSeparator = false;
                    break;
            }
        }

        builder.Append('$');
        return builder.ToString();
    }
}
