// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Npgsql;

namespace Tests;

internal static class RestrictedPostgreSqlDatabase
{
    public static async Task ExecuteAsync(
        bool preinstallTrgm,
        Func<string, Task> assertion,
        CancellationToken cancellationToken
    )
    {
        // The restricted login is a role, which lives on the server rather than in a database, so it gets a server of
        // its own and leaves with it.
        await using var server = new PrivatePostgreSqlServer();
        await server.StartAsync();

        var role = $"messaging_app_{Guid.NewGuid():N}";
        const string password = "restricted-test-password";
        var database = server.DatabaseName;
        string? restrictedConnectionString = null;

        await using var adminConnection = new NpgsqlConnection(server.AdminConnectionString);
        await adminConnection.OpenAsync(cancellationToken);

        try
        {
            await adminConnection.ExecuteAsync(
                new CommandDefinition(
                    $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}';",
                    cancellationToken: cancellationToken
                )
            );

            await using (var databaseAdminConnection = new NpgsqlConnection(server.ConnectionString))
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
                        $"CREATE SCHEMA headless AUTHORIZATION \"{role}\";",
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

            var restrictedBuilder = new NpgsqlConnectionStringBuilder(server.ConnectionString)
            {
                Username = role,
                Password = password,
            };
            restrictedConnectionString = restrictedBuilder.ConnectionString;
            await assertion(restrictedConnectionString);
        }
        finally
        {
            // Disposing the server removes the role and the database; only this process's pooled sessions need closing.
            if (restrictedConnectionString is not null)
            {
                await using var restrictedConnection = new NpgsqlConnection(restrictedConnectionString);
                NpgsqlConnection.ClearPool(restrictedConnection);
            }
        }
    }
}
