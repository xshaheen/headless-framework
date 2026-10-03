// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

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
