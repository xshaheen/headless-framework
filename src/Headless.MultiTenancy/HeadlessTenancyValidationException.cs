// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// Thrown at host startup when tenant posture validation produces one or more
/// <see cref="HeadlessTenancyDiagnosticSeverity.Error"/> diagnostics. Inherits
/// <see cref="InvalidOperationException"/> so existing catch sites still match.
/// </summary>
/// <remarks>Creates the exception with the failing diagnostics attached.</remarks>
/// <param name="message">The aggregate failure message.</param>
/// <param name="diagnostics">The startup-blocking diagnostics that caused validation to fail.</param>
/// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
[PublicAPI]
public sealed class HeadlessTenancyValidationException(
    string message,
    IReadOnlyList<HeadlessTenancyDiagnostic> diagnostics
) : InvalidOperationException(message)
{
    /// <summary>The startup-blocking diagnostics that caused validation to fail.</summary>
    public IReadOnlyList<HeadlessTenancyDiagnostic> Diagnostics { get; } = Argument.IsNotNull(diagnostics);
}
