// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Hosting;

namespace Headless.MultiTenancy;

internal sealed class HeadlessTenancyStartupValidator(
    IEnumerable<IHeadlessTenancyValidator> validators,
    IServiceProvider serviceProvider,
    TenantPostureManifest manifest
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var context = new HeadlessTenancyValidationContext(
            Argument.IsNotNull(serviceProvider),
            Argument.IsNotNull(manifest)
        );

        var failures = Argument
            .IsNotNull(validators)
            .SelectMany(validator => validator.Validate(context))
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
}
