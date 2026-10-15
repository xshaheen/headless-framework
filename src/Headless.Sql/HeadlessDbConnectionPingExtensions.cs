// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace System.Data.Common;

/// <summary>
/// Connectivity probe for a <see cref="DbConnection" /> the caller already holds, such as one built from a feature's
/// own connection options. Declared in the augmented type's namespace so the probe is discoverable wherever a
/// <see cref="DbConnection" /> is in scope.
/// </summary>
[PublicAPI]
public static class HeadlessDbConnectionPingExtensions
{
    extension(DbConnection connection)
    {
        /// <summary>
        /// Runs <c>SELECT 1</c> on the open connection, proving that the server answers queries on the connection's
        /// database.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the probe.</param>
        /// <returns>A task that completes when the server answered.</returns>
        /// <remarks>
        /// A query is sent because opening a pooled connection can hand back an idle connection without any round
        /// trip, which would report a dead server as reachable. The caller opens the connection first, and owns and
        /// disposes it.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The connection is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">The connection is not open.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
        /// <exception cref="DbException">The server is unreachable or rejected the query.</exception>
        public Task PingAsync(CancellationToken cancellationToken = default)
        {
            return connection.PingAsync("SELECT 1", cancellationToken);
        }

        /// <summary>
        /// Runs <paramref name="commandText" /> on the open connection as a scalar query, for a probe that must prove
        /// more than connectivity, such as a query against a table the application needs.
        /// </summary>
        /// <param name="commandText">The query to run. Its result is discarded.</param>
        /// <param name="cancellationToken">Token to cancel the probe.</param>
        /// <returns>A task that completes when the server answered.</returns>
        /// <remarks>The caller opens the connection first, and owns and disposes it.</remarks>
        /// <exception cref="ArgumentNullException">The connection is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException"><paramref name="commandText" /> is <see langword="null" />, empty, or whitespace.</exception>
        /// <exception cref="InvalidOperationException">The connection is not open.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
        /// <exception cref="DbException">The server is unreachable or rejected the query.</exception>
        public async Task PingAsync(string commandText, CancellationToken cancellationToken = default)
        {
            Argument.IsNotNull(connection);
            Argument.IsNotNullOrWhiteSpace(commandText);

            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // False positive: the probe runs the application's own configured query, never request input.
            command.CommandText = commandText;
#pragma warning restore CA2100
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
