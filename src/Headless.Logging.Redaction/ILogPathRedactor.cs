// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Logging;

/// <summary>
/// Declares request paths that carry a credential, such as a signed URL's token, so every place the framework logs or
/// traces a request path can replace it: ASP.NET Core's request scope (<c>RequestPath</c>) and its "Request starting"
/// and "Request finished" lines, HTTP logging, span tags, and the framework's own log entries that name a path.
/// </summary>
/// <remarks>
/// Register an implementation with <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ILogPathRedactor, T&gt;())</c>
/// and call <c>AddHeadlessLogRedaction()</c>. The hosting layer writes these entries before any middleware runs, so
/// the logger factory is the only place they can be rewritten. HTTP logging and span tags record the path separately:
/// <c>AddHeadlessRequestLogRedaction</c> and <c>AddHeadlessLogPathRedaction</c> from
/// <c>Headless.Logging.Redaction.AspNetCore</c> cover them.
/// </remarks>
[PublicAPI]
public interface ILogPathRedactor
{
    /// <summary>Replaces the credential part of <paramref name="path"/> when the path carries one.</summary>
    /// <param name="path">The request path as the hosting layer logs it, starting with <c>/</c>.</param>
    /// <param name="redacted">The path with its credential replaced, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> carried a credential and was rewritten.</returns>
    bool TryRedact(string path, [NotNullWhen(true)] out string? redacted);

    /// <summary>Applies the first of <paramref name="redactors"/> that recognizes <paramref name="path"/>.</summary>
    /// <param name="redactors">The registered redactors, in registration order.</param>
    /// <param name="path">The request path, starting with <c>/</c>.</param>
    /// <param name="redacted">The rewritten path, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when one of <paramref name="redactors"/> rewrote <paramref name="path"/>.</returns>
    static bool TryRedactAny(
        IReadOnlyList<ILogPathRedactor> redactors,
        string path,
        [NotNullWhen(true)] out string? redacted
    )
    {
        Argument.IsNotNull(redactors);

        foreach (var redactor in redactors)
        {
            if (redactor.TryRedact(path, out redacted))
            {
                return true;
            }
        }

        redacted = null;

        return false;
    }

    /// <summary>
    /// Returns <paramref name="path"/> with the first matching redactor applied, or <paramref name="path"/> itself
    /// when none recognizes it, for a log entry that names the request path.
    /// </summary>
    /// <param name="redactors">The registered redactors, in registration order.</param>
    /// <param name="path">The request path, starting with <c>/</c>.</param>
    /// <returns>The path safe to log.</returns>
    static string RedactAny(IReadOnlyList<ILogPathRedactor> redactors, string path)
    {
        return TryRedactAny(redactors, path, out var redacted) ? redacted : path;
    }
}
