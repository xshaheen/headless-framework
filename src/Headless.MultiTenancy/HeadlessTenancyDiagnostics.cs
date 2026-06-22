// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Severity for a tenant posture diagnostic.</summary>
[PublicAPI]
public enum HeadlessTenancyDiagnosticSeverity
{
    /// <summary>Informational diagnostic.</summary>
    Information,

    /// <summary>Warning diagnostic.</summary>
    Warning,

    /// <summary>Startup-blocking diagnostic.</summary>
    Error,
}

/// <summary>Non-PII diagnostic emitted by tenant posture validators.</summary>
[PublicAPI]
public sealed record HeadlessTenancyDiagnostic
{
    /// <summary>Creates a diagnostic, validating that the seam, code, and message are non-blank.</summary>
    /// <param name="seam">The seam that produced the diagnostic.</param>
    /// <param name="code">A stable diagnostic code.</param>
    /// <param name="message">A non-PII diagnostic message.</param>
    /// <param name="severity">The diagnostic severity.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="seam"/>, <paramref name="code"/>, or <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="seam"/>, <paramref name="code"/>, or <paramref name="message"/> is empty or white space.
    /// </exception>
    public HeadlessTenancyDiagnostic(
        string seam,
        string code,
        string message,
        HeadlessTenancyDiagnosticSeverity severity
    )
    {
        Seam = Argument.IsNotNullOrWhiteSpace(seam);
        Code = Argument.IsNotNullOrWhiteSpace(code);
        Message = Argument.IsNotNullOrWhiteSpace(message);
        Severity = severity;
    }

    /// <summary>The seam that produced the diagnostic.</summary>
    public string Seam { get; }

    /// <summary>A stable diagnostic code.</summary>
    public string Code { get; }

    /// <summary>A non-PII diagnostic message.</summary>
    public string Message { get; }

    /// <summary>The diagnostic severity.</summary>
    public HeadlessTenancyDiagnosticSeverity Severity { get; }

    /// <summary>Creates a startup-blocking diagnostic.</summary>
    /// <param name="seam">The seam that produced the diagnostic.</param>
    /// <param name="code">A stable diagnostic code.</param>
    /// <param name="message">A non-PII diagnostic message.</param>
    /// <returns>An <see cref="HeadlessTenancyDiagnosticSeverity.Error"/> diagnostic.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Any argument is empty or white space.</exception>
    public static HeadlessTenancyDiagnostic Error(string seam, string code, string message)
    {
        return new(seam, code, message, HeadlessTenancyDiagnosticSeverity.Error);
    }

    /// <summary>Creates a non-blocking warning diagnostic.</summary>
    /// <param name="seam">The seam that produced the diagnostic.</param>
    /// <param name="code">A stable diagnostic code.</param>
    /// <param name="message">A non-PII diagnostic message.</param>
    /// <returns>A <see cref="HeadlessTenancyDiagnosticSeverity.Warning"/> diagnostic.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Any argument is empty or white space.</exception>
    public static HeadlessTenancyDiagnostic Warning(string seam, string code, string message)
    {
        return new(seam, code, message, HeadlessTenancyDiagnosticSeverity.Warning);
    }

    /// <summary>Creates an informational diagnostic.</summary>
    /// <param name="seam">The seam that produced the diagnostic.</param>
    /// <param name="code">A stable diagnostic code.</param>
    /// <param name="message">A non-PII diagnostic message.</param>
    /// <returns>An <see cref="HeadlessTenancyDiagnosticSeverity.Information"/> diagnostic.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Any argument is empty or white space.</exception>
    public static HeadlessTenancyDiagnostic Information(string seam, string code, string message)
    {
        return new(seam, code, message, HeadlessTenancyDiagnosticSeverity.Information);
    }
}

/// <summary>
/// Thrown at host startup when tenant posture validation produces one or more
/// <see cref="HeadlessTenancyDiagnosticSeverity.Error"/> diagnostics. Inherits
/// <see cref="InvalidOperationException"/> so existing catch sites still match.
/// </summary>
[PublicAPI]
public sealed class HeadlessTenancyValidationException : InvalidOperationException
{
    /// <summary>Creates the exception with the failing diagnostics attached.</summary>
    /// <param name="message">The aggregate failure message.</param>
    /// <param name="diagnostics">The startup-blocking diagnostics that caused validation to fail.</param>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
    public HeadlessTenancyValidationException(string message, IReadOnlyList<HeadlessTenancyDiagnostic> diagnostics)
        : base(message)
    {
        Diagnostics = Argument.IsNotNull(diagnostics);
    }

    /// <summary>The startup-blocking diagnostics that caused validation to fail.</summary>
    public IReadOnlyList<HeadlessTenancyDiagnostic> Diagnostics { get; }
}

/// <summary>Validation context passed to tenant posture validators.</summary>
/// <param name="Services">The application service provider.</param>
/// <param name="Manifest">The shared tenant posture manifest.</param>
[PublicAPI]
public sealed record HeadlessTenancyValidationContext(IServiceProvider Services, TenantPostureManifest Manifest);

/// <summary>Validates tenant posture at host startup.</summary>
[PublicAPI]
public interface IHeadlessTenancyValidator
{
    /// <summary>Validates tenant posture and returns non-PII diagnostics.</summary>
    /// <param name="context">The validation context.</param>
    /// <returns>Non-PII diagnostics; an empty sequence when the seam this validator owns is healthy.</returns>
    /// <remarks>
    /// Implementations must return non-PII diagnostics only and should be cheap and bounded (no blocking
    /// I/O), since they run synchronously during host startup. Throwing is permitted: the startup runner
    /// catches any exception other than <see cref="OperationCanceledException"/> and converts it into a
    /// synthetic <c>VALIDATOR_THREW</c> error diagnostic so one faulty validator cannot mask the others;
    /// an <see cref="OperationCanceledException"/> is allowed to propagate to honor host shutdown.
    /// </remarks>
    IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context);
}
