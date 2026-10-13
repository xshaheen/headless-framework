// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// The readiness check each PostgreSQL Messaging store contributes, one per outbox, so an unreachable outbox database
/// shows up under its own name.
/// </summary>
/// <remarks>
/// The probe opens one connection from the outbox's own options and runs <c>SELECT 1</c> on it. The query is needed
/// because opening a pooled connection can hand back an idle connection without any round trip, which would report a
/// stopped database as reachable. It reads nothing and writes nothing: the schema belongs to the schema runner.
/// </remarks>
internal static class PostgreSqlMessagingStorageHealthCheck
{
    private const string _NamePrefix = "messaging-storage-postgresql-";

    public const string PrimaryCheckName = _NamePrefix + "default";

    /// <summary>Names the check of an additional outbox after its ordinal, which is unique within the host.</summary>
    /// <param name="outboxOptionsName">The outbox's named-options key, which ends with its ordinal.</param>
    public static string OutboxCheckName(string outboxOptionsName) =>
        _NamePrefix + "outbox-" + outboxOptionsName[(outboxOptionsName.LastIndexOf('.') + 1)..];

    public static void Add(IServiceCollection services, string name, Func<IServiceProvider, PostgreSqlOptions> options)
    {
        services.AddHeadlessHealthCheck(
            name,
            async (provider, cancellationToken) =>
            {
                var connection = options(provider).CreateConnection();
                await using (connection.ConfigureAwait(false))
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await connection.PingAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            HeadlessHealthCheckTags.Messaging,
            HeadlessHealthCheckTags.Database
        );
    }
}
