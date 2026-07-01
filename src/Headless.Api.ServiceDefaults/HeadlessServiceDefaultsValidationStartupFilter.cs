// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Headless.Api;

/// <summary>
/// Startup filter and hosted-lifecycle service that verifies Headless pipeline call-order invariants
/// before the first request is handled. Runs during both the legacy <see cref="IStartupFilter"/> path
/// (ASP.NET Core) and the hosted-lifecycle <see cref="IHostedLifecycleService.StartingAsync"/> path (.NET Generic Host).
/// </summary>
/// <remarks>
/// Throws <see cref="InvalidOperationException"/> at startup when any of the following checks fail
/// (each can be disabled via the corresponding option):
/// <list type="bullet">
///   <item><see cref="HeadlessServiceDefaultsValidationOptions.RequireUseHeadless"/> — <c>UseHeadless()</c> was not called.</item>
///   <item><see cref="HeadlessServiceDefaultsValidationOptions.RequireMapHeadlessEndpoints"/> — <c>MapHeadlessEndpoints()</c> was not called.</item>
///   <item><see cref="HeadlessServiceDefaultsValidationOptions.RequireStatusCodesRewriter"/> — <c>UseStatusCodesRewriter()</c> (or <c>UseHeadless()</c>) was not called.</item>
/// </list>
/// </remarks>
internal sealed class HeadlessServiceDefaultsValidationStartupFilter(
    HeadlessServiceDefaultsOptions options,
    HeadlessStartupState state
) : IStartupFilter, IHostedLifecycleService
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            _Validate(options, state);

            next(app);
        };
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _Validate(options, state);

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
