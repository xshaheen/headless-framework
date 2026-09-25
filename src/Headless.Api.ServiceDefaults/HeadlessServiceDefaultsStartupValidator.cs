// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Validation;

namespace Headless.Api.ServiceDefaults;

/// <summary>
/// Startup validator that verifies the Headless pipeline calls were made before the host starts serving requests.
/// </summary>
/// <remarks>
/// Throws <see cref="InvalidOperationException"/> at startup naming every missing call; each check can be disabled
/// through its option:
/// <list type="bullet">
///   <item><see cref="HeadlessServiceDefaultsValidationOptions.RequireUseHeadless"/> — <c>UseHeadless()</c> was not called.</item>
///   <item><see cref="HeadlessServiceDefaultsValidationOptions.RequireMapHeadlessEndpoints"/> — <c>MapHeadlessEndpoints()</c> was not called.</item>
///   <item><see cref="HeadlessServiceDefaultsValidationOptions.RequireStatusCodesRewriter"/> — <c>UseStatusCodesRewriter()</c> (or <c>UseHeadless()</c>) was not called.</item>
/// </list>
/// The application configures its pipeline on the built <c>WebApplication</c> before it starts the host, so every
/// call has been recorded in <see cref="HeadlessStartupState"/> by the time validators run.
/// </remarks>
internal sealed class HeadlessServiceDefaultsStartupValidator(
    HeadlessServiceDefaultsOptions options,
    HeadlessStartupState state
) : IStartupValidator
{
    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">One or more required pipeline calls were not made.</exception>
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        var missing = new List<string>();

        if (options.Validation.RequireUseHeadless && !state.UseHeadlessCalled)
        {
            missing.Add("Call UseHeadless before the application starts.");
        }

        if (options.Validation.RequireMapHeadlessEndpoints && !state.MapHeadlessEndpointsCalled)
        {
            missing.Add("Call MapHeadlessEndpoints before the application starts.");
        }

        if (options.Validation.RequireStatusCodesRewriter && !state.UseStatusCodesRewriterCalled)
        {
            missing.Add("Call UseStatusCodesRewriter (or UseHeadless) before the application starts.");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(string.Join(' ', missing));
        }

        return Task.CompletedTask;
    }
}
