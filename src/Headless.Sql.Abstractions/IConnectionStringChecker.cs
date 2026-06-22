// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

/// <summary>
/// Probes a connection string to determine whether the server is reachable and the target database exists.
/// </summary>
/// <remarks>
/// Intended for startup health checks and environment-validation scenarios, not for hot-path code.
/// Implementations use a short connect timeout (typically 1 second) so a misconfigured or unreachable
/// server fails fast. Failures are logged as warnings and surfaced through the returned tuple rather
/// than thrown, so callers do not need to catch provider-specific exceptions.
/// </remarks>
[PublicAPI]
public interface IConnectionStringChecker
{
    /// <summary>
    /// Attempts to connect to the server and verify that the target database exists.
    /// </summary>
    /// <param name="connectionString">The ADO.NET connection string to probe.</param>
    /// <returns>
    /// A tuple where <c>Connected</c> is <see langword="true"/> when the server accepted the
    /// connection, and <c>DatabaseExists</c> is <see langword="true"/> when the named database
    /// was found on that server. Both are <see langword="false"/> when a connection error occurs.
    /// </returns>
    Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString);
}
