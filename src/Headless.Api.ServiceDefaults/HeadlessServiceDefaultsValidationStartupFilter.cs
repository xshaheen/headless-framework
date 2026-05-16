// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Headless.Api;

internal sealed class HeadlessServiceDefaultsValidationStartupFilter(HeadlessServiceDefaultsOptions options)
    : IStartupFilter,
        IHostedLifecycleService
{
    private readonly HeadlessServiceDefaultsOptions _options = options;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            _Validate(_options);

            next(app);
        };
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _Validate(_options);

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void _Validate(HeadlessServiceDefaultsOptions options)
    {
        if (options.Validation.RequireUseHeadless && !options.UseHeadlessCalled)
        {
            throw new InvalidOperationException("Call UseHeadless before the application starts.");
        }

        if (options.Validation.RequireMapHeadlessEndpoints && !options.MapHeadlessEndpointsCalled)
        {
            throw new InvalidOperationException("Call MapHeadlessEndpoints before the application starts.");
        }
    }
}
