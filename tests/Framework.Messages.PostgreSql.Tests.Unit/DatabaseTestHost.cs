using Dapper;
using Framework.Messages.Persistence;

namespace Tests;

public abstract class DatabaseTestHost : TestHost
{
    private static bool _sqlObjectInstalled;
    private static Lock Lock { get; } = new();

    protected override void PostBuildServices()
    {
        base.PostBuildServices();

        lock (Lock)
        {
            if (!_sqlObjectInstalled)
            {
                _InitializeDatabase();
            }
        }
    }

    public override void Dispose()
    {
        _DeleteAllData();
        base.Dispose();
    }

    private void _InitializeDatabase()
    {
        using (CreateScope())
        {
            var storage = GetService<IStorageInitializer>();
            _CreateDatabase();
            storage.InitializeAsync().GetAwaiter().GetResult();
            _sqlObjectInstalled = true;
        }
    }

    private void _CreateDatabase()
    {
        var masterConn = ConnectionUtil.GetMasterConnectionString();
        var databaseName = ConnectionUtil.GetDatabaseName();
        using var connection = ConnectionUtil.CreateConnection(masterConn);

        connection.Execute(
            $"""
            DROP DATABASE IF EXISTS {databaseName};
            CREATE DATABASE {databaseName};
            """
        );
    }

    private static void _DeleteAllData()
    {
        var conn = ConnectionUtil.GetConnectionString();

        using var connection = ConnectionUtil.CreateConnection(conn);

        connection.Execute(
            """
            TRUNCATE TABLE cap.published;
            TRUNCATE TABLE cap.received;
            """
        );
    }
}
