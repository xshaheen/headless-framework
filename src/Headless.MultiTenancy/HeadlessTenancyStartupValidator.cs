// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
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
    private const string DiagnosticsExceptionDataKey = "HeadlessTenancyDiagnostics";

    public Task StartingAsync(CancellationToken cancellationToken) => _ValidateAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Task _ValidateAsync(CancellationToken cancellationToken)
    {
        var context = new HeadlessTenancyValidationContext(
            Argument.IsNotNull(serviceProvider),
            Argument.IsNotNull(manifest)
        );

        var collected = new List<HeadlessTenancyDiagnostic>();

        foreach (var validator in Argument.IsNotNull(validators))
        {
            try
            {
                foreach (var diagnostic in validator.Validate(context))
                {
                    collected.Add(diagnostic);
                }
            }
#pragma warning disable CA1031, EPC12 // Last-resort fallback path: a single misbehaving validator must not abort the iteration; full exception detail flows through the synthetic diagnostic message and the validator-error log on the failure path.
            catch (Exception validatorError)
#pragma warning restore CA1031, EPC12
            {
                LogTenancyValidatorThrew(logger, validatorError, validator.GetType().Name);
                collected.Add(
                    HeadlessTenancyDiagnostic.Error(
                        seam: "Validator",
                        code: "VALIDATOR_THREW",
                        message: $"{validator.GetType().Name}: {validatorError.Message}"
                    )
                );
            }
        }

        var diagnostics = collected.ToArray();
        var failures = diagnostics
            .Where(diagnostic => diagnostic.Severity == HeadlessTenancyDiagnosticSeverity.Error)
            .ToArray();
        var warnings = diagnostics
            .Where(diagnostic => diagnostic.Severity == HeadlessTenancyDiagnosticSeverity.Warning)
            .ToArray();
        var infos = diagnostics
            .Where(diagnostic => diagnostic.Severity == HeadlessTenancyDiagnosticSeverity.Information)
            .ToArray();

        if (failures.Length > 0)
        {
            // Errors first so operators see the blocking diagnostics ahead of any non-blocking noise.
            foreach (var error in failures)
            {
                LogTenancyDiagnosticError(logger, error.Code, error.Seam, error.Message);
            }

            foreach (var warning in warnings)
            {
                LogTenancyDiagnosticWarning(logger, warning.Code, warning.Seam, warning.Message);
            }

            var exception = new InvalidOperationException(
                "Headless tenancy validation failed: "
                    + string.Join("; ", failures.Select(failure => $"{failure.Code}: {failure.Message}"))
            );

            exception.Data[DiagnosticsExceptionDataKey] = failures;

            throw exception;
        }

        foreach (var info in infos)
        {
            LogTenancyDiagnosticInformation(logger, info.Code, info.Seam, info.Message);
        }

        foreach (var warning in warnings)
        {
            LogTenancyDiagnosticWarning(logger, warning.Code, warning.Seam, warning.Message);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 4,
        EventName = "HeadlessTenancyValidatorThrew",
        Level = LogLevel.Error,
        Message = "Headless tenancy validator {ValidatorType} threw during Validate; raising synthetic VALIDATOR_THREW diagnostic."
    )]
    private static partial void LogTenancyValidatorThrew(ILogger logger, Exception exception, string validatorType);

    [LoggerMessage(
        EventId = 1,
        EventName = "HeadlessTenancyDiagnosticInformation",
        Level = LogLevel.Information,
        Message = "Headless tenancy diagnostic ({Code}) on seam {Seam}: {DiagnosticMessage}"
    )]
    private static partial void LogTenancyDiagnosticInformation(
        ILogger logger,
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
    private static partial void LogTenancyDiagnosticWarning(
        ILogger logger,
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
    private static partial void LogTenancyDiagnosticError(
        ILogger logger,
        string code,
        string seam,
        string diagnosticMessage
    );
}
