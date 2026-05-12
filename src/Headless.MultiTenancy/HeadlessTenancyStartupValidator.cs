// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.MultiTenancy;

/// <summary>Validates configured tenant posture at host startup and logs non-PII diagnostics from all registered validators.</summary>
/// <remarks>
/// <para>
/// Registered as an <see cref="IHostedService"/> by <see cref="HeadlessTenancyServiceCollectionExtensions.AddHeadlessTenancyCore"/>.
/// <c>StartAsync</c> runs validators registered as <see cref="IHeadlessTenancyValidator"/>; any diagnostic
/// with <see cref="HeadlessTenancyDiagnosticSeverity.Error"/> throws and prevents the host from starting.
/// </para>
/// <para>
/// Test-harness note: integration tests that build the host (for example <c>WebApplicationFactory</c>)
/// will execute this validator at <c>StartAsync</c>. Tests that exercise HTTP tenancy must include
/// <c>UseHeadlessTenancy()</c> in their pipeline so <c>HeadlessHttpTenancyValidator</c> sees the runtime
/// marker; otherwise the startup will fail with <c>HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING</c>. Tests
/// that need to skip validation entirely should not call <c>AddHeadlessTenancy(...)</c>, or should compose
/// only the seams they exercise. A future <c>SkipRuntimeValidation</c> escape hatch is tracked under issue
/// follow-up #29 in the PR #245 review.
/// </para>
/// </remarks>
internal sealed partial class HeadlessTenancyStartupValidator(
    IEnumerable<IHeadlessTenancyValidator> validators,
    IServiceProvider serviceProvider,
    TenantPostureManifest manifest,
    ILogger<HeadlessTenancyStartupValidator> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var context = new HeadlessTenancyValidationContext(
            Argument.IsNotNull(serviceProvider),
            Argument.IsNotNull(manifest)
        );

        var diagnostics = Argument.IsNotNull(validators).SelectMany(validator => validator.Validate(context)).ToArray();

        foreach (var diagnostic in diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case HeadlessTenancyDiagnosticSeverity.Information:
                    LogTenancyDiagnosticInformation(logger, diagnostic.Code, diagnostic.Seam, diagnostic.Message);
                    break;
                case HeadlessTenancyDiagnosticSeverity.Warning:
                    LogTenancyDiagnosticWarning(logger, diagnostic.Code, diagnostic.Seam, diagnostic.Message);
                    break;
                case HeadlessTenancyDiagnosticSeverity.Error:
                    LogTenancyDiagnosticError(logger, diagnostic.Code, diagnostic.Seam, diagnostic.Message);
                    break;
            }
        }

        var failures = diagnostics
            .Where(diagnostic => diagnostic.Severity == HeadlessTenancyDiagnosticSeverity.Error)
            .ToArray();

        if (failures.Length > 0)
        {
            throw new InvalidOperationException(
                "Headless tenancy validation failed: "
                    + string.Join("; ", failures.Select(failure => $"{failure.Code}: {failure.Message}"))
            );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
