// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Supplies a <see cref="CancellationToken"/> representing the lifetime or cancellation scope of the
/// current ambient context — for example, an HTTP request's <c>RequestAborted</c> token or an
/// application-shutdown token. Inject this into services that must cooperate with request cancellation
/// without receiving the token directly as a method parameter.
/// </summary>
public interface ICancellationTokenProvider
{
    /// <summary>Gets the cancellation token for the current ambient scope.</summary>
    CancellationToken Token { get; }
}
