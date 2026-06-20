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
/// <param name="Seam">The seam that produced the diagnostic.</param>
/// <param name="Code">A stable diagnostic code.</param>
/// <param name="Message">A non-PII diagnostic message.</param>
/// <param name="Severity">The diagnostic severity.</param>
[PublicAPI]
public sealed record HeadlessTenancyDiagnostic(
    string Seam,
    string Code,
    string Message,
    HeadlessTenancyDiagnosticSeverity Severity
)
{
    /// <summary>Creates a startup-blocking diagnostic.</summary>
    public static HeadlessTenancyDiagnostic Error(string seam, string code, string message)
    {
        return _Create(seam, code, message, HeadlessTenancyDiagnosticSeverity.Error);
    }

    /// <summary>Creates a non-blocking warning diagnostic.</summary>
    public static HeadlessTenancyDiagnostic Warning(string seam, string code, string message)
    {
        return _Create(seam, code, message, HeadlessTenancyDiagnosticSeverity.Warning);
    }

    /// <summary>Creates an informational diagnostic.</summary>
    public static HeadlessTenancyDiagnostic Information(string seam, string code, string message)
    {
        return _Create(seam, code, message, HeadlessTenancyDiagnosticSeverity.Information);
    }

    private static HeadlessTenancyDiagnostic _Create(
        string seam,
        string code,
        string message,
        HeadlessTenancyDiagnosticSeverity severity
    )
    {
        return new(
            Argument.IsNotNullOrWhiteSpace(seam),
            Argument.IsNotNullOrWhiteSpace(code),
            Argument.IsNotNullOrWhiteSpace(message),
            severity
        );
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
    /// <returns>Non-PII diagnostics.</returns>
    IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context);
}
