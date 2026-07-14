// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Npgsql;

namespace Tests;

internal static class RestrictedPostgreSqlDatabase
{
    public static async Task ExecuteAsync(
        string adminConnectionString,
        bool preinstallTrgm,
        Func<string, Task> assertion,
        CancellationToken cancellationToken
    )
    {
        var suffix = Guid.NewGuid().ToString("N");
        var database = $"messages_restricted_{suffix}";
        var role = $"messaging_app_{suffix}";
        const string password = "restricted-test-password";
        var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = "postgres" };
        var roleCreated = false;
        var databaseCreated = false;
        string? restrictedConnectionString = null;

        await using var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync(cancellationToken);

        try
        {
            await adminConnection.ExecuteAsync(
                new CommandDefinition(
                    $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}';",
                    cancellationToken: cancellationToken
                )
            );
            roleCreated = true;
            await adminConnection.ExecuteAsync(
                new CommandDefinition($"CREATE DATABASE \"{database}\";", cancellationToken: cancellationToken)
            );
            databaseCreated = true;

            var databaseAdminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = database };
            await using (var databaseAdminConnection = new NpgsqlConnection(databaseAdminBuilder.ConnectionString))
            {
                await databaseAdminConnection.OpenAsync(cancellationToken);
                await databaseAdminConnection.ExecuteAsync(
                    new CommandDefinition(
                        $"GRANT CREATE ON DATABASE \"{database}\" TO \"{role}\";",
                        cancellationToken: cancellationToken
                    )
                );
                await databaseAdminConnection.ExecuteAsync(
                    new CommandDefinition(
                        $"CREATE SCHEMA messaging AUTHORIZATION \"{role}\";",
                        cancellationToken: cancellationToken
                    )
                );
                if (preinstallTrgm)
                {
                    await databaseAdminConnection.ExecuteAsync(
                        new CommandDefinition("CREATE EXTENSION pg_trgm;", cancellationToken: cancellationToken)
                    );
                }

                await databaseAdminConnection.ExecuteAsync(
                    new CommandDefinition(
                        $$"""
                        CREATE FUNCTION deny_ext_{{role}}() RETURNS event_trigger
                        LANGUAGE plpgsql
                        AS $function$
                        BEGIN
                            IF session_user = '{{role}}' THEN
                                RAISE EXCEPTION 'CREATE EXTENSION is managed by the database administrator'
                                    USING ERRCODE = 'insufficient_privilege';
                            END IF;
                        END;
                        $function$;

                        CREATE EVENT TRIGGER deny_ext_{{role}}
                            ON ddl_command_start
                            WHEN TAG IN ('CREATE EXTENSION')
                            EXECUTE FUNCTION deny_ext_{{role}}();
                        """,
                        cancellationToken: cancellationToken
                    )
                );
            }

            var restrictedBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = database,
                Username = role,
                Password = password,
            };
            restrictedConnectionString = restrictedBuilder.ConnectionString;
            await assertion(restrictedConnectionString);
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30), TimeProvider.System);
            var cleanupToken = cleanupCts.Token;

            if (restrictedConnectionString is not null)
            {
                await using var restrictedConnection = new NpgsqlConnection(restrictedConnectionString);
                NpgsqlConnection.ClearPool(restrictedConnection);
            }

            if (databaseCreated)
            {
                await adminConnection.ExecuteAsync(
                    new CommandDefinition(
                        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @Database AND pid <> pg_backend_pid();",
                        new { Database = database },
                        cancellationToken: cleanupToken
                    )
                );
                await adminConnection.ExecuteAsync(
                    new CommandDefinition($"DROP DATABASE IF EXISTS \"{database}\";", cancellationToken: cleanupToken)
                );
            }

            if (roleCreated)
            {
                await adminConnection.ExecuteAsync(
                    new CommandDefinition($"DROP ROLE IF EXISTS \"{role}\";", cancellationToken: cleanupToken)
                );
            }
        }
    }
}
