// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Headless.Api;

internal sealed class HeadlessServiceDefaultsValidationStartupFilter(
    HeadlessServiceDefaultsOptions options,
    HeadlessStartupState state
) : IStartupFilter, IHostedLifecycleService
{
    private readonly HeadlessServiceDefaultsOptions _options = options;
    private readonly HeadlessStartupState _state = state;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            _Validate(_options, _state);

            next(app);
        };
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _Validate(_options, _state);

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void _Validate(HeadlessServiceDefaultsOptions options, HeadlessStartupState state)
    {
        if (options.Validation.RequireUseHeadless && !state.UseHeadlessCalled)
        {
            throw new InvalidOperationException("Call UseHeadless before the application starts.");
        }

        if (options.Validation.RequireMapHeadlessEndpoints && !state.MapHeadlessEndpointsCalled)
        {
            throw new InvalidOperationException("Call MapHeadlessEndpoints before the application starts.");
        }

        if (options.Validation.RequireStatusCodesRewriter && !state.UseStatusCodesRewriterCalled)
        {
            throw new InvalidOperationException(
                "Call UseStatusCodesRewriter (or UseHeadless) before the application starts."
            );
        }
    }
}
