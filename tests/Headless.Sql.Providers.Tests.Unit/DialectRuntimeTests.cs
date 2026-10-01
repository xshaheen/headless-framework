// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.PostgreSql;
using Headless.Sql.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Npgsql;
using NpgsqlTypes;

namespace Tests;

public sealed class DialectRuntimeTests : TestBase
{
    [Fact]
    public void should_name_identifiers_in_each_engines_convention()
    {
        PostgreSqlDialect.Instance.Name("PK_FencingLeases").Should().Be("pk_fencing_leases");
        PostgreSqlDialect.Instance.Name("TakeoverCount").Should().Be("takeover_count");
        SqlServerDialect.Instance.Name("PK_FencingLeases").Should().Be("PK_FencingLeases");
    }

    [Fact]
    public void should_quote_and_escape_identifiers()
    {
        PostgreSqlDialect.Instance.Qualify("headless", "a\"b").Should().Be("\"headless\".\"a\"\"b\"");
        SqlServerDialect.Instance.Qualify("headless", "a]b").Should().Be("[headless].[a]]b]");
    }

    [Fact]
    public void should_draw_a_sequence_value_in_each_engines_syntax()
    {
        PostgreSqlDialect.Instance.NextSequenceValue("\"s\".\"q'x\"").Should().Be("nextval('\"s\".\"q''x\"')");
        SqlServerDialect.Instance.NextSequenceValue("[s].[q]").Should().Be("NEXT VALUE FOR [s].[q]");
    }

    [Fact]
    public void should_compare_keysets_as_tuples()
    {
        PostgreSqlDialect.Instance.KeysetAfter(["a", "b"], ["A", "B"]).Should().Be("(a, b) > (@A, @B)");
        SqlServerDialect.Instance.KeysetAfter(["a", "b"], ["A", "B"]).Should().Be("((a > @A) OR (a = @A AND b > @B))");
    }

    [Fact]
    public void should_bind_a_postgresql_duration_as_one_interval()
    {
        using var command = new NpgsqlCommand();

        PostgreSqlDialect.Instance.AddDuration(command, "D", TimeSpan.FromDays(3));

        command.Parameters.Should().ContainSingle();
        command.Parameters[0].NpgsqlDbType.Should().Be(NpgsqlDbType.Interval);
        PostgreSqlDialect.Instance.ShiftByDuration("x", "D", subtract: true).Should().Be("x - @D");
    }

    [Fact]
    public void should_bind_a_sql_server_duration_in_parts_no_int_can_overflow()
    {
        using var command = new SqlCommand();
        var duration = TimeSpan.FromDays(400) + TimeSpan.FromSeconds(70) + TimeSpan.FromTicks(15);

        SqlServerDialect.Instance.AddDuration(command, "D", duration);

        command.Parameters["DDays"].Value.Should().Be(400);
        command.Parameters["DSeconds"].Value.Should().Be(70);
        command.Parameters["DNanoseconds"].Value.Should().Be(1500);
        SqlServerDialect.Instance.ShiftByDuration("x", "D").Should().Contain("DATEADD(day, @DDays, x)");
    }

    [Fact]
    public void should_bind_postgresql_instants_in_utc_and_bytes_and_nulls()
    {
        using var command = new NpgsqlCommand();
        var instant = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(3));

        PostgreSqlDialect.Instance.AddParameter(command, "T", SqlColumnType.Timestamp, instant);
        PostgreSqlDialect.Instance.AddParameter(command, "B", SqlColumnType.Binary, new ReadOnlyMemory<byte>([1, 2]));
        PostgreSqlDialect.Instance.AddParameter(command, "N", SqlColumnType.Text(10), value: null);
        PostgreSqlDialect.Instance.AddParameter(command, "K", SqlColumnType.KeyText(10), "key");

        ((DateTimeOffset)command.Parameters["T"].Value!).Offset.Should().Be(TimeSpan.Zero);
        command.Parameters["B"].Value.Should().BeEquivalentTo(new byte[] { 1, 2 });
        command.Parameters["N"].Value.Should().Be(DBNull.Value);
        command.Parameters["K"].NpgsqlDbType.Should().Be(NpgsqlDbType.Varchar);
    }

    [Fact]
    public void should_size_sql_server_text_parameters_to_their_columns()
    {
        using var command = new SqlCommand();

        SqlServerDialect.Instance.AddParameter(command, "K", SqlColumnType.KeyText(64), "key");
        SqlServerDialect.Instance.AddParameter(command, "B", SqlColumnType.Binary, new ReadOnlyMemory<byte>([1]));
        SqlServerDialect.Instance.AddParameter(command, "I", SqlColumnType.Int64, 5L);

        command.Parameters["K"].Size.Should().Be(64);
        command.Parameters["B"].Size.Should().Be(-1);
        command.Parameters["B"].Value.Should().BeEquivalentTo(new byte[] { 1 });
    }

    [Fact]
    public void should_type_every_column_kind_for_each_engine()
    {
        using var pg = new NpgsqlCommand();
        using var ss = new SqlCommand();
        var kinds = new (SqlColumnType Type, object Value, NpgsqlDbType Pg, System.Data.SqlDbType Ss)[]
        {
            (SqlColumnType.KeyText(8), "k", NpgsqlDbType.Varchar, System.Data.SqlDbType.NVarChar),
            (SqlColumnType.Text(8), "t", NpgsqlDbType.Varchar, System.Data.SqlDbType.NVarChar),
            (SqlColumnType.Int16, (short)1, NpgsqlDbType.Smallint, System.Data.SqlDbType.SmallInt),
            (SqlColumnType.Int32, 1, NpgsqlDbType.Integer, System.Data.SqlDbType.Int),
            (SqlColumnType.Int64, 1L, NpgsqlDbType.Bigint, System.Data.SqlDbType.BigInt),
            (
                SqlColumnType.Timestamp,
                DateTimeOffset.UnixEpoch,
                NpgsqlDbType.TimestampTz,
                System.Data.SqlDbType.DateTimeOffset
            ),
            (SqlColumnType.Binary, new byte[] { 1 }, NpgsqlDbType.Bytea, System.Data.SqlDbType.VarBinary),
            (SqlColumnType.Guid, Guid.Empty, NpgsqlDbType.Uuid, System.Data.SqlDbType.UniqueIdentifier),
            (SqlColumnType.Boolean, true, NpgsqlDbType.Boolean, System.Data.SqlDbType.Bit),
            (SqlColumnType.Json, "{}", NpgsqlDbType.Jsonb, System.Data.SqlDbType.NVarChar),
        };

        for (var i = 0; i < kinds.Length; i++)
        {
            var name = "P" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            PostgreSqlDialect.Instance.AddParameter(pg, name, kinds[i].Type, kinds[i].Value);
            SqlServerDialect.Instance.AddParameter(ss, name, kinds[i].Type, kinds[i].Value);

            pg.Parameters[name].NpgsqlDbType.Should().Be(kinds[i].Pg);
            ss.Parameters[name].SqlDbType.Should().Be(kinds[i].Ss);
        }

        SqlServerDialect.Instance.AddParameter(ss, "Null", SqlColumnType.Int32, value: null);
        ss.Parameters["Null"].Value.Should().Be(DBNull.Value);
        var unknown = new SqlColumnType((SqlColumnKind)99);
        FluentActions
            .Invoking(() => SqlServerDialect.Instance.AddParameter(ss, "X", unknown, 1))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => PostgreSqlDialect.Instance.AddParameter(pg, "X", unknown, 1))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_bind_a_postgresql_list_as_a_typed_array_with_utc_instants()
    {
        using var command = new NpgsqlCommand();
        var instant = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(3));

        PostgreSqlDialect.Instance.AddListParameter(command, "Keys", SqlColumnType.KeyText(8), new[] { "a", "b" });
        PostgreSqlDialect.Instance.AddListParameter(command, "At", SqlColumnType.Timestamp, new[] { instant });

        command.Parameters["Keys"].Value.Should().BeOfType<string[]>();
        command.Parameters["Keys"].Value.Should().BeEquivalentTo(new[] { "a", "b" });
        ((DateTimeOffset[])command.Parameters["At"].Value!)[0].Offset.Should().Be(TimeSpan.Zero);
        FluentActions
            .Invoking(() =>
                PostgreSqlDialect.Instance.AddListParameter(command, "B", SqlColumnType.Binary, new[] { new byte[1] })
            )
            .Should()
            .Throw<ArgumentException>();
    }

    [Fact]
    public void should_bind_a_sql_server_list_as_one_json_array()
    {
        using var command = new SqlCommand();

        SqlServerDialect.Instance.AddListParameter(command, "Ids", SqlColumnType.Int64, new[] { 1L, 2L });

        command.Parameters["Ids"].SqlDbType.Should().Be(System.Data.SqlDbType.NVarChar);
        command.Parameters["Ids"].Size.Should().Be(-1);
        command.Parameters["Ids"].Value.Should().Be("[1,2]");
        FluentActions
            .Invoking(() =>
                SqlServerDialect.Instance.AddListParameter(command, "B", SqlColumnType.Binary, new[] { new byte[1] })
            )
            .Should()
            .Throw<ArgumentException>();
    }

    [Fact]
    public void should_create_a_list_parameter_without_a_command()
    {
        var ids = new[] { Guid.Empty };

        var pg = PostgreSqlDialect.Instance.CreateListParameter("Ids", SqlColumnType.Guid, ids);
        var ss = SqlServerDialect.Instance.CreateListParameter("Ids", SqlColumnType.Guid, ids);

        pg.Should().BeOfType<NpgsqlParameter>().Which.Value.Should().BeEquivalentTo(ids);
        ss.Should().BeOfType<SqlParameter>().Which.Value.Should().Be("[\"00000000-0000-0000-0000-000000000000\"]");
        FluentActions
            .Invoking(() => SqlServerDialect.Instance.CreateListParameter("J", SqlColumnType.Json, new[] { "{}" }))
            .Should()
            .Throw<ArgumentException>();
    }

    [Fact]
    public void should_expose_each_engines_provider_types()
    {
        PostgreSqlDialect.Instance.ConnectionType.Should().Be<NpgsqlConnection>();
        PostgreSqlDialect.Instance.TransactionType.Should().Be<NpgsqlTransaction>();
        SqlServerDialect.Instance.ConnectionType.Should().Be<SqlConnection>();
        SqlServerDialect.Instance.TransactionType.Should().Be<SqlTransaction>();
        PostgreSqlDialect.Instance.DisplayName.Should().Be("PostgreSQL");
        SqlServerDialect.Instance.DisplayName.Should().Be("SQL Server");
    }

    [Theory]
    [InlineData("40P01", SqlErrorKind.Deadlock)]
    [InlineData("40001", SqlErrorKind.SerializationConflict)]
    [InlineData("23505", SqlErrorKind.UniqueViolation)]
    [InlineData("42P07", SqlErrorKind.DuplicateObject)]
    [InlineData("55P03", SqlErrorKind.LockTimeout)]
    [InlineData("25P02", SqlErrorKind.TransactionAborted)]
    [InlineData("22021", SqlErrorKind.None)]
    public void should_classify_postgresql_errors_by_sqlstate(string sqlState, SqlErrorKind expected)
    {
        var error = new PostgresException("boom", "ERROR", "ERROR", sqlState);

        PostgreSqlDialect.Instance.Classify(error).Should().Be(expected);
    }

    [Fact]
    public void should_classify_foreign_exceptions_as_none()
    {
        PostgreSqlDialect.Instance.Classify(new InvalidOperationException()).Should().Be(SqlErrorKind.None);
        SqlServerDialect.Instance.Classify(new InvalidOperationException()).Should().Be(SqlErrorKind.None);
    }

    [Fact]
    public void should_create_each_engines_connection()
    {
        using var pg = PostgreSqlDialect.Instance.CreateConnection("Host=localhost;Database=d");
        using var ss = SqlServerDialect.Instance.CreateConnection("Server=localhost;Database=d");

        pg.Should().BeOfType<NpgsqlConnection>();
        ss.Should().BeOfType<SqlConnection>();
        PostgreSqlDialect.Instance.TimestampPrecision.Should().Be(TimeSpan.FromMicroseconds(1));
        SqlServerDialect.Instance.TimestampPrecision.Should().Be(TimeSpan.FromTicks(1));
    }
}
