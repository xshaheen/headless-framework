// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Logging;

/// <summary>
/// Declares request paths that carry a credential, such as a signed URL's token, so the log redaction wrapper can
/// replace them in ASP.NET Core's request scope (<c>RequestPath</c>) and its "Request starting" and "Request finished"
/// lines.
/// </summary>
/// <remarks>
/// Register an implementation with <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ILogPathRedactor, T&gt;())</c>
/// and call <c>AddHeadlessLogRedaction()</c>. The hosting layer writes these entries before any middleware runs, so
/// the logger factory is the only place they can be rewritten. HTTP logging and span tags record the path separately;
/// the feature that owns the path covers them.
/// </remarks>
[PublicAPI]
public interface ILogPathRedactor
{
    /// <summary>Replaces the credential part of <paramref name="path"/> when the path carries one.</summary>
    /// <param name="path">The request path as the hosting layer logs it, starting with <c>/</c>.</param>
    /// <param name="redacted">The path with its credential replaced, when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> carried a credential and was rewritten.</returns>
    bool TryRedact(string path, [NotNullWhen(true)] out string? redacted);
}
