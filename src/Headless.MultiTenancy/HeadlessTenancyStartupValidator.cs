// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.MultiTenancy;

/// <summary>Validates configured tenant posture at host startup and logs non-PII diagnostics from all registered validators.</summary>
/// <remarks>
/// Registered as an <see cref="IHostedLifecycleService"/> by the Headless tenancy root setup so the
/// validator's <see cref="StartingAsync"/> step runs synchronously BEFORE any other hosted service's
/// <see cref="IHostedService.StartAsync"/>. Otherwise a misconfigured tenancy posture could allow
/// downstream hosted services (background workers, messaging consumers, …) to begin processing under
/// the wrong tenant assumptions before this validator failed the host.
/// Any diagnostic with <see cref="HeadlessTenancyDiagnosticSeverity.Error"/> throws and prevents the
/// host from starting. A validator that throws during enumeration is reported as a synthetic
/// <c>VALIDATOR_THREW</c> error diagnostic so a single buggy validator cannot mask issues from other
/// validators.
/// </remarks>
internal sealed partial class HeadlessTenancyStartupValidator(
    IEnumerable<IHeadlessTenancyValidator> validators,
    IServiceProvider serviceProvider,
    TenantPostureManifest manifest,
    ILogger<HeadlessTenancyStartupValidator> logger
) : IHostedLifecycleService
{
    /// <inheritdoc/>
    /// <remarks>
    /// Runs all registered validators synchronously before any other hosted service starts. The work is
    /// synchronous, so a failure throws directly (the returned task is never observed in the failure case).
    /// </remarks>
    /// <exception cref="HeadlessTenancyValidationException">
    /// One or more validators produced an <see cref="HeadlessTenancyDiagnosticSeverity.Error"/> diagnostic;
    /// the host is prevented from starting.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // Validation is synchronous and must complete (or fail the host) before any other hosted
        // service starts. A synchronous throw here surfaces to the host the same way a faulted task
        // would; it carries the typed HeadlessTenancyValidationException with the failing diagnostics.
        _Validate(cancellationToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void _Validate(CancellationToken cancellationToken)
    {
        var context = new HeadlessTenancyValidationContext(serviceProvider, manifest);

        var collected = new List<HeadlessTenancyDiagnostic>();

        foreach (var validator in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                collected.AddRange(validator.Validate(context));
            }
#pragma warning disable CA1031, EPC12 // Last-resort fallback path: a single misbehaving validator must not abort the iteration; full exception detail flows through the synthetic diagnostic message and the validator-error log on the failure path. Host-shutdown cancellation (OperationCanceledException) is excluded by the filter so it propagates instead of being masked as a validator error.
            catch (Exception validatorError) when (validatorError is not OperationCanceledException)
#pragma warning restore CA1031, EPC12
            {
                logger.LogTenancyValidatorThrew(validatorError, validator.GetType().Name);

                // Keep the synthetic diagnostic non-PII: the exception message could carry tenant
                // identifiers, and this text flows into the manifest-facing diagnostic and the thrown
                // exception. Full exception detail is preserved in the LogTenancyValidatorThrew call above.
                collected.Add(
                    HeadlessTenancyDiagnostic.Error(
                        seam: "Validator",
                        code: "VALIDATOR_THREW",
                        message: $"{validator.GetType().Name} threw during validation; see logs for detail."
                    )
                );
            }
        }

        var failures = collected
            .Where(diagnostic => diagnostic.Severity == HeadlessTenancyDiagnosticSeverity.Error)
            .ToArray();
        var warnings = collected
            .Where(diagnostic => diagnostic.Severity == HeadlessTenancyDiagnosticSeverity.Warning)
            .ToArray();
        var infos = collected
            .Where(diagnostic => diagnostic.Severity == HeadlessTenancyDiagnosticSeverity.Information)
            .ToArray();

        // Log every collected diagnostic exactly once — errors first so operators see the blocking
        // diagnostics ahead of non-blocking noise — on both the success and failure paths. Logging
        // infos before the throw keeps the informational context that is most useful for diagnosing
        // a startup failure.
        foreach (var error in failures)
        {
            logger.LogTenancyDiagnosticError(error.Code, error.Seam, error.Message);
        }

        foreach (var warning in warnings)
        {
            logger.LogTenancyDiagnosticWarning(warning.Code, warning.Seam, warning.Message);
        }

        foreach (var info in infos)
        {
            logger.LogTenancyDiagnosticInformation(info.Code, info.Seam, info.Message);
        }

        if (failures.Length > 0)
        {
            var errorText = string.Join("; ", failures.Select(failure => $"{failure.Code}: {failure.Message}"));

            throw new HeadlessTenancyValidationException($"Headless tenancy validation failed: {errorText}", failures);
        }
    }
}

internal static partial class HeadlessTenancyStartupValidatorLogger
{
    [LoggerMessage(
        EventId = 4,
        EventName = "HeadlessTenancyValidatorThrew",
        Level = LogLevel.Error,
        Message = "Headless tenancy validator {ValidatorType} threw during Validate; raising synthetic VALIDATOR_THREW diagnostic."
    )]
    public static partial void LogTenancyValidatorThrew(this ILogger logger, Exception exception, string validatorType);

    [LoggerMessage(
        EventId = 1,
        EventName = "HeadlessTenancyDiagnosticInformation",
        Level = LogLevel.Information,
        Message = "Headless tenancy diagnostic ({Code}) on seam {Seam}: {DiagnosticMessage}"
    )]
    public static partial void LogTenancyDiagnosticInformation(
        this ILogger logger,
        string code,
        string seam,
        string diagnosticMessage
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "HeadlessTenancyDiagnosticWarning",
        Level = LogLevel.Warning,
        Message = "Headless tenancy diagnostic ({Code}) on seam {Seam}: {DiagnosticMessage}"
    )]
    public static partial void LogTenancyDiagnosticWarning(
        this ILogger logger,
        string code,
        string seam,
        string diagnosticMessage
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "HeadlessTenancyDiagnosticError",
        Level = LogLevel.Error,
        Message = "Headless tenancy diagnostic ({Code}) on seam {Seam}: {DiagnosticMessage}"
    )]
    public static partial void LogTenancyDiagnosticError(
        this ILogger logger,
        string code,
        string seam,
        string diagnosticMessage
    );
}
