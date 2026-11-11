// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.AspNetCore.Http;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Fails startup when the <see cref="IHttpContextAccessor" /> the container resolves is not the one live endpoints
/// wrapped.
/// </summary>
/// <remarks>
/// <c>UseEndpoints()</c> wraps the accessor registered when it runs, and <c>AddHttpContextAccessor()</c> called
/// later adds nothing. An accessor registered afterwards with a plain <c>AddSingleton</c> or a replace takes the
/// wrapper's place, and every run would then read the request that opened the stream: the handler and
/// <c>ICurrentUser</c> would see the principal the stream opened with, not the one the run just authenticated.
/// </remarks>
internal sealed class LiveRunHttpContextAccessorValidator(IHttpContextAccessor accessor) : IHeadlessStartupValidator
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Another accessor replaced the one live endpoints wrapped.</exception>
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (accessor is not LiveRunHttpContextAccessor)
        {
            throw new InvalidOperationException(
                $"Headless live endpoints wrap the registered IHttpContextAccessor so a run reads its own request, but '{accessor.GetType().FullName}' was registered after UseEndpoints() and replaced the wrapper. Register a custom IHttpContextAccessor before AddHeadlessLiveQueries(setup => setup.UseEndpoints())."
            );
        }

        return Task.CompletedTask;
    }
}
