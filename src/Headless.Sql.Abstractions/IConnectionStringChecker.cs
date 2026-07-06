// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

/// <summary>
/// Probes a connection string to determine whether the server is reachable and the target database exists.
/// </summary>
/// <remarks>
/// Intended for startup health checks and environment-validation scenarios, not for hot-path code.
/// Implementations use a short connect timeout (typically 1 second) so a misconfigured or unreachable
/// server fails fast. Failures are logged as warnings and surfaced through the returned
/// <see cref="ConnectionCheckResult"/> rather than thrown, so callers do not need to catch
/// provider-specific exceptions.
/// </remarks>
[PublicAPI]
public interface IConnectionStringChecker
{
    /// <summary>
    /// Attempts to connect to the server and verify that the target database exists.
    /// </summary>
    /// <param name="connectionString">The ADO.NET connection string to probe.</param>
    /// <param name="cancellationToken">Token to cancel the probe.</param>
    /// <returns>
    /// A <see cref="ConnectionCheckResult"/> whose <see cref="ConnectionCheckResult.Connected"/> is
    /// <see langword="true"/> when the server accepted the connection, and whose
    /// <see cref="ConnectionCheckResult.DatabaseExists"/> is <see langword="true"/> when the named
    /// database was found on that server. Both are <see langword="false"/> when a connection error occurs.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the probe completes.
    /// </exception>
    Task<ConnectionCheckResult> CheckAsync(string connectionString, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of an <see cref="IConnectionStringChecker.CheckAsync"/> probe.
/// </summary>
/// <param name="Connected">
/// <see langword="true"/> when the server accepted the connection; otherwise <see langword="false"/>.
/// </param>
/// <param name="DatabaseExists">
/// <see langword="true"/> when the target database was found on the server; otherwise <see langword="false"/>.
/// </param>
[PublicAPI]
public readonly record struct ConnectionCheckResult(bool Connected, bool DatabaseExists);
