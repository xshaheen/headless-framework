// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Severity for a tenant posture diagnostic.</summary>
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
        return new(
            Argument.IsNotNullOrWhiteSpace(seam),
            Argument.IsNotNullOrWhiteSpace(code),
            Argument.IsNotNullOrWhiteSpace(message),
            HeadlessTenancyDiagnosticSeverity.Error
        );
    }
}

/// <summary>Validation context passed to tenant posture validators.</summary>
/// <param name="Services">The application service provider.</param>
/// <param name="Manifest">The shared tenant posture manifest.</param>
public sealed record HeadlessTenancyValidationContext(IServiceProvider Services, TenantPostureManifest Manifest);

/// <summary>Validates tenant posture at host startup.</summary>
public interface IHeadlessTenancyValidator
{
    /// <summary>Validates tenant posture and returns non-PII diagnostics.</summary>
    /// <param name="context">The validation context.</param>
    /// <returns>Non-PII diagnostics.</returns>
    IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context);
}
