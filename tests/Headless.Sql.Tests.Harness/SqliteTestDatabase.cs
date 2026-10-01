// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.Sqlite;

namespace Tests;

/// <summary>
/// A SQLite database in its own temporary directory, deleted on dispose. A file rather than <c>:memory:</c>, so every
/// connection a test opens reaches the same database, as separate processes would.
/// </summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    private SqliteTestDatabase(string directory, string connectionString)
    {
        _directory = directory;
        ConnectionString = connectionString;
    }

    /// <summary>The connection string; its data source is the file's resolved path.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates the database file in a new temporary directory.</summary>
    public static SqliteTestDatabase Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "headless-sqlite-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var requested = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "test.db") };

        // SQLite reports the path with symbolic links resolved (/var is /private/var on macOS); the unit-of-work
        // database check compares data sources, so every connection string the test builds names that same path.
        string resolved;

        using (var connection = new SqliteConnection(requested.ConnectionString))
        {
            connection.Open();
            resolved = connection.DataSource;
            connection.Close();
            SqliteConnection.ClearPool(connection);
        }

        return new SqliteTestDatabase(
            directory,
            new SqliteConnectionStringBuilder { DataSource = resolved }.ToString()
        );
    }

    public async ValueTask DisposeAsync()
    {
        await using (var connection = new SqliteConnection(ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A connection a failed test leaked still holds the file; the directory is in the temp area regardless.
        }
    }
}
