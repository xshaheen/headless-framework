// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Headless.Sql.PostgreSql;
using Headless.Sql.Sqlite;
using Headless.Sql.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Headless.Sql.Benchmarks;

/// <summary>
/// Binds one <see cref="ReadOnlyMemory{T}" /> payload the way a stored idempotent response or a wrapped key is bound,
/// without a connection: the allocation per bind is what the dialect costs on top of the payload the caller owns.
/// </summary>
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifecycle: GlobalCleanup disposes the commands.
[MemoryDiagnoser]
public class BinaryParameterBenchmarks
{
    private readonly NpgsqlCommand _postgreSql = new();
    private readonly SqlCommand _sqlServer = new();
    private readonly SqliteCommand _sqlite = new();
    private ReadOnlyMemory<byte> _payload;

    [Params(1024, 64 * 1024, 1024 * 1024)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var bytes = new byte[Size];
        Random.Shared.NextBytes(bytes);
        _payload = bytes;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _postgreSql.Dispose();
        _sqlServer.Dispose();
        _sqlite.Dispose();
    }

    [Benchmark]
    public object? PostgreSql() => _Bind(PostgreSqlDialect.Instance, _postgreSql);

    [Benchmark]
    public object? SqlServer() => _Bind(SqlServerDialect.Instance, _sqlServer);

    [Benchmark]
    public object? Sqlite() => _Bind(SqliteDialect.Instance, _sqlite);

    private object? _Bind(ISqlDialect dialect, DbCommand command)
    {
        command.Parameters.Clear();
        dialect.AddParameter(command, "Result", SqlColumnType.Binary, _payload);

        return command.Parameters[0].Value;
    }
}
#pragma warning restore CA1001
