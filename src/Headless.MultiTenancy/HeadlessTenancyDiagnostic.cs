// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Severity for a tenant posture diagnostic.</summary>
[PublicAPI]
public enum HeadlessTenancyDiagnosticSeverity
{
    /// <summary>Informational diagnostic.</summary>
    Information = 0,

    /// <summary>Warning diagnostic.</summary>
    Warning = 1,

    /// <summary>Startup-blocking diagnostic.</summary>
    Error = 2,
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
