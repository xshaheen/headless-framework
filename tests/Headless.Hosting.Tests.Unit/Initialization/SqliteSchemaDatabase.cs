// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Diagnostics;
using Headless.Hosting.Initialization.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Initialization;

/// <summary>
/// A private in-memory SQLite database the schema runner can apply to, with hooks for the lock, a concurrent-DDL race,
/// and the span a statement runs under. SQLite has no schemas, so the dialect prefixes each table with its schema.
/// </summary>
internal sealed class SqliteSchemaDatabase : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private int _racesLeft;

    public SqliteSchemaDatabase()
    {
        ConnectionString = $"Data Source=schema-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
        Dialect = new SqliteSchemaDialect();
    }

    public string ConnectionString { get; }

    public ISchemaDialect Dialect { get; }

    public FakeTimeProvider Clock { get; } = new();

    /// <summary>How long another runner holds the lock: the clock advances by it before the lock is granted.</summary>
    public TimeSpan LockHeldFor { get; set; }

    /// <summary>Whether the lock is never granted.</summary>
    public bool LockUnavailable { get; set; }

    /// <summary>The span the last <c>SELECT capture_activity()</c> statement ran under.</summary>
    public Activity? CapturedActivity { get; private set; }

    /// <summary>Makes the next <paramref name="count" /> <c>SELECT race_once()</c> statements fail as a lost race.</summary>
    public void RaceNext(int count) => _racesLeft = count;

    public SchemaContribution Contribution(string feature, string schema, params SchemaStep[] steps)
    {
        return new SchemaContribution(feature, Dialect, _CreateConnection, schema, steps);
    }

    public void Execute(string sql)
    {
        using var command = _keepAlive.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _keepAlive.Dispose();

    private SqliteConnection _CreateConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.CreateFunction<string, bool>("try_lock", _ => _TryLock());
        connection.CreateFunction("race_once", _RaceOnce);
        connection.CreateFunction("capture_activity", _CaptureActivity);

        return connection;
    }

    private bool _TryLock()
    {
        if (LockUnavailable)
        {
            return false;
        }

        Clock.Advance(LockHeldFor);

        return true;
    }

    private int _RaceOnce()
    {
        if (_racesLeft <= 0)
        {
            return 0;
        }

        _racesLeft--;

        throw new InvalidOperationException("table widgets already exists");
    }

    private int _CaptureActivity()
    {
        CapturedActivity = Activity.Current;

        return 0;
    }

    private sealed class SqliteSchemaDialect : ISchemaDialect
    {
        public string Name => "Sqlite";

        public string? ScriptBatchSeparator => null;

        public string TryAcquireLockSql => "SELECT try_lock(@LockResource)";

        public string ReleaseLockSql => "SELECT 1 WHERE @LockResource IS NOT NULL";

        public string DatabaseIdentity(DbConnection connection) => connection.ConnectionString;

        public DbParameter CreateStringParameter(string name, string value) => new SqliteParameter("@" + name, value);

        public string HistoryTableSql(string schema) =>
            $"CREATE TABLE IF NOT EXISTS {schema}_history (feature TEXT, version TEXT, checksum TEXT, description TEXT, PRIMARY KEY (feature, version))";

        public string HistoryTableName(string schema) => $"{schema}_history";

        public string ReadHistorySql(string schema) =>
            $"SELECT feature, version, checksum, description FROM {schema}_history";

        public string InsertHistorySql(string schema) =>
            $"INSERT OR IGNORE INTO {schema}_history VALUES (@Feature, @StepVersion, @Checksum, @Description)";

        public bool IsAlreadyCreatedRace(Exception exception) =>
            exception is SqliteException && exception.Message.Contains("already exists", StringComparison.Ordinal);

        public bool IsObjectNotFound(Exception exception) =>
            exception is SqliteException && exception.Message.Contains("no such table", StringComparison.Ordinal);
    }
}
