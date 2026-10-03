// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// <see cref="ICancellationTokenProvider"/> implementation that always returns <see cref="CancellationToken.None"/>.
/// Use as a no-op default in singleton services, background jobs, or tests where no real cancellation
/// scope exists. Exposed as a singleton via <see cref="Instance"/> to avoid unnecessary allocations.
/// </summary>
public sealed class DefaultCancellationTokenProvider : ICancellationTokenProvider
{
    /// <summary>Gets the shared singleton instance.</summary>
    public static DefaultCancellationTokenProvider Instance { get; } = new();

    private DefaultCancellationTokenProvider() { }

    /// <inheritdoc/>
    public CancellationToken Token => CancellationToken.None;
}
