// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The host's <see cref="IHttpContextAccessor" />, answering with a run's own request while that run executes.
/// </summary>
/// <remarks>
/// A run cannot be published through the host's accessor. Its setter first empties the holder that every frame of
/// the opening request shares, so the middleware above the endpoint and the transport would read
/// <see langword="null" /> from then on. The run is kept here instead, in state that only the run's own calls
/// carry, and everything else still reads the host's accessor.
/// </remarks>
/// <param name="inner">The accessor the host registered.</param>
internal sealed class LiveRunHttpContextAccessor(IHttpContextAccessor inner) : IHttpContextAccessor
{
    private static readonly AsyncLocal<Run?> _Current = new();

    /// <summary>
    /// Gets the request of the run this code executes in, else what the host's accessor returns. Work a run started
    /// and left running reads <see langword="null" /> once that run has returned: the run's request is gone with
    /// its scope, and the request that opened the stream is not the one that work was started for.
    /// </summary>
    public HttpContext? HttpContext
    {
        get => _Current.Value is { } run ? run.Request : inner.HttpContext;
        // The host's own state: the server assigns a request here when it begins and clears it when it ends.
        set => inner.HttpContext = value;
    }

    /// <summary>
    /// Makes <paramref name="request" /> the current request of one run. Call it synchronously inside the async
    /// method that awaits the endpoint: the runtime then scopes it to that method and the calls it makes, and the
    /// caller reads the host's accessor again when the method returns.
    /// </summary>
    public static Run Begin(HttpContext request)
    {
        var run = new Run(request);
        _Current.Value = run;

        return run;
    }

    /// <summary>One run's request, for as long as the run executes.</summary>
    internal sealed class Run(HttpContext request)
    {
        private volatile HttpContext? _request = request;

        public HttpContext? Request => _request;

        /// <summary>
        /// Ends the run. A task the handler left running still carries this object for the rest of its life, and
        /// must not keep reading a request whose scope is disposed.
        /// </summary>
        public void End()
        {
            _request = null;
        }
    }
}
