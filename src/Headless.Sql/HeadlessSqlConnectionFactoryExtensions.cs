// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sql;

/// <summary>Connectivity helpers for <see cref="ISqlConnectionFactory" />.</summary>
[PublicAPI]
public static class HeadlessSqlConnectionFactoryExtensions
{
    extension(ISqlConnectionFactory factory)
    {
        /// <summary>
        /// Opens a new connection and runs <c>SELECT 1</c> on it, proving that the server answers queries on the
        /// configured database. Health checks use it as their probe.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the probe.</param>
        /// <returns>A task that completes when the server answered.</returns>
        /// <remarks>
        /// A query is sent because opening a pooled connection can hand back an idle connection without any round
        /// trip, which would report a dead server as reachable.
        /// </remarks>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
        /// <exception cref="System.Data.Common.DbException">The server is unreachable or rejected the query.</exception>
        public async Task PingAsync(CancellationToken cancellationToken = default)
        {
            Argument.IsNotNull(factory);

            await using var connection = await factory
                .CreateNewConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
