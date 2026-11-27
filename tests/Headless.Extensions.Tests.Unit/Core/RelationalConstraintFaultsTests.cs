// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;

namespace Tests.Core;

/// <summary>
/// The foreign-key, CHECK, NOT NULL, length, and numeric-overflow checks of <see cref="RelationalFaults" />, each read
/// from the engine's own code space.
/// </summary>
public sealed class RelationalConstraintFaultsTests : TestBase
{
    private const string _SqlServerForeignKeyInsert =
        "The INSERT statement conflicted with the FOREIGN KEY constraint \"FK_Orders_Members_MemberId\". The conflict occurred in database \"shop\", table \"dbo.Members\", column 'Id'.";

    private const string _SqlServerReferenceDelete =
        "The DELETE statement conflicted with the REFERENCE constraint \"FK_Orders_Members_MemberId\". The conflict occurred in database \"shop\", table \"dbo.Orders\", column 'MemberId'.";

    private const string _SqlServerSameTableReference =
        "The DELETE statement conflicted with the SAME TABLE REFERENCE constraint \"FK_Nodes_Nodes_ParentId\". The conflict occurred in database \"shop\", table \"dbo.Nodes\", column 'ParentId'.";

    private const string _SqlServerCheck =
        "The INSERT statement conflicted with the CHECK constraint \"CK_Orders_Total\". The conflict occurred in database \"shop\", table \"dbo.Orders\", column 'Total'.";

    [Theory]
    [InlineData("23503", true)] // foreign_key_violation: a missing row, or a NO ACTION key refusing a delete
    [InlineData("23001", true)] // restrict_violation: a RESTRICT key refusing a delete
    [InlineData("23505", false)]
    [InlineData("23514", false)]
    [InlineData(null, false)]
    public void should_recognize_a_postgresql_foreign_key_violation_by_sqlstate(string? sqlState, bool expected)
    {
        RelationalFaults.IsReferenceViolation(new FakeDbException(sqlState: sqlState)).Should().Be(expected);
    }

    [Theory]
    [InlineData("23503")]
    [InlineData("23001")]
    public void should_read_the_violated_foreign_key_name_from_a_postgresql_driver(string sqlState)
    {
        var exception = new InvalidOperationException(
            "save failed",
            new Npgsql.PostgresException(sqlState, "FK_Orders_Members_MemberId")
        );

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be("FK_Orders_Members_MemberId");
    }

    [Theory]
    [InlineData(_SqlServerForeignKeyInsert)]
    [InlineData(_SqlServerReferenceDelete)]
    [InlineData(_SqlServerSameTableReference)]
    public void should_read_the_foreign_key_name_from_a_sql_server_message(string message)
    {
        var exception = new SqlException(new SqlError(547, message));

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        RelationalFaults.IsCheckViolation(exception).Should().BeFalse();
        constraintName.Should().Match("FK_*");
    }

    [Fact]
    public void should_tell_a_sql_server_check_violation_from_a_foreign_key_that_shares_its_number()
    {
        var exception = new SqlException(new SqlError(547, _SqlServerCheck));

        RelationalFaults.IsCheckViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be("CK_Orders_Total");
        RelationalFaults.IsReferenceViolation(exception).Should().BeFalse();
    }

    [Fact]
    public void should_count_a_localized_sql_server_547_as_a_foreign_key_without_a_name()
    {
        // The number alone cannot tell a foreign key from a CHECK constraint; a foreign key is the common case.
        var exception = new SqlException(
            new SqlError(547, "Die DELETE-Anweisung steht in Konflikt mit der REFERENCE-Einschränkung.")
        );

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().BeNull();
        RelationalFaults.IsCheckViolation(exception).Should().BeFalse();
    }

    [Fact]
    public void should_read_the_foreign_key_from_the_error_that_reported_it_when_it_is_not_the_first()
    {
        var exception = new SqlException(
            new SqlError(3621, "The statement has been terminated."),
            new SqlError(547, _SqlServerReferenceDelete)
        );

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be("FK_Orders_Members_MemberId");
    }

    [Fact]
    public void should_read_the_foreign_key_from_a_legacy_sql_server_message()
    {
        var exception = new System.Data.SqlClient.SqlException(547, _SqlServerReferenceDelete);

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be("FK_Orders_Members_MemberId");
    }

    [Fact]
    public void should_not_read_a_sql_server_constraint_number_from_another_drivers_exception()
    {
        // A number means nothing outside SqlClient.
        var exception = new FakeDbException(number: 547);

        RelationalFaults.IsReferenceViolation(exception).Should().BeFalse();
        RelationalFaults.IsCheckViolation(exception).Should().BeFalse();
    }

    [Theory]
    [InlineData(19, 787, true)] // SQLITE_CONSTRAINT_FOREIGNKEY
    [InlineData(19, 2067, false)] // SQLITE_CONSTRAINT_UNIQUE
    [InlineData(19, 275, false)] // SQLITE_CONSTRAINT_CHECK
    public void should_recognize_a_sqlite_foreign_key_violation_by_extended_result_code(
        int errorCode,
        int extendedErrorCode,
        bool expected
    )
    {
        var exception = new Microsoft.Data.Sqlite.SqliteException(errorCode, extendedErrorCode);

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().Be(expected);
        constraintName.Should().BeNull();
    }

    [Theory]
    [InlineData("SQLite Error 19: 'FOREIGN KEY constraint failed'.", true)]
    [InlineData("SQLite Error 19: 'orders are closed'.", false)] // a user trigger's RAISE(ABORT, ...)
    public void should_recognize_a_sqlite_restrict_refusal_by_its_trigger_code_and_message(
        string message,
        bool expected
    )
    {
        var exception = new Microsoft.Data.Sqlite.SqliteException(19, 1811, message);

        RelationalFaults.IsReferenceViolation(exception).Should().Be(expected);
    }

    [Fact]
    public void should_recognize_a_postgresql_check_violation_with_its_name()
    {
        var exception = new Npgsql.PostgresException("23514", "CK_Orders_Total");

        RelationalFaults.IsCheckViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be("CK_Orders_Total");
        RelationalFaults.IsReferenceViolation(exception).Should().BeFalse();
    }

    [Fact]
    public void should_recognize_a_sqlite_check_violation_without_a_name()
    {
        var exception = new Microsoft.Data.Sqlite.SqliteException(19, 275);

        RelationalFaults.IsCheckViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().BeNull();
    }

    [Fact]
    public void should_recognize_a_not_null_violation_on_each_engine()
    {
        RelationalFaults.IsNotNullViolation(new FakeDbException(sqlState: "23502")).Should().BeTrue();
        RelationalFaults.IsNotNullViolation(new SqlException(3621, furtherNumbers: [515])).Should().BeTrue();
        RelationalFaults.IsNotNullViolation(new Microsoft.Data.Sqlite.SqliteException(19, 1299)).Should().BeTrue();

        RelationalFaults.IsNotNullViolation(new FakeDbException(sqlState: "23503")).Should().BeFalse();
        RelationalFaults.IsNotNullViolation(new FakeDbException(number: 515)).Should().BeFalse();
        RelationalFaults.IsNotNullViolation(new Microsoft.Data.Sqlite.SqliteException(19, 787)).Should().BeFalse();
    }

    [Theory]
    [InlineData(8152, true)]
    [InlineData(2628, true)]
    [InlineData(8115, false)]
    public void should_recognize_a_sql_server_value_too_long_in_any_error(int number, bool expected)
    {
        RelationalFaults.IsMaxLengthExceeded(new SqlException(3621, furtherNumbers: [number])).Should().Be(expected);
    }

    [Fact]
    public void should_recognize_a_value_too_long_on_postgresql_and_sqlite()
    {
        RelationalFaults.IsMaxLengthExceeded(new FakeDbException(sqlState: "22001")).Should().BeTrue();
        RelationalFaults.IsMaxLengthExceeded(new Microsoft.Data.Sqlite.SqliteException(18, 18)).Should().BeTrue();

        RelationalFaults.IsMaxLengthExceeded(new FakeDbException(sqlState: "22003")).Should().BeFalse();
        RelationalFaults.IsMaxLengthExceeded(new FakeDbException(number: 8152)).Should().BeFalse();
    }

    [Theory]
    [InlineData(8115, true)]
    [InlineData(220, true)]
    [InlineData(8152, false)]
    public void should_recognize_a_sql_server_numeric_overflow_in_any_error(int number, bool expected)
    {
        RelationalFaults.IsNumericOverflow(new SqlException(3621, furtherNumbers: [number])).Should().Be(expected);
    }

    [Fact]
    public void should_recognize_a_postgresql_numeric_overflow_and_none_on_sqlite()
    {
        RelationalFaults.IsNumericOverflow(new FakeDbException(sqlState: "22003")).Should().BeTrue();
        RelationalFaults.IsNumericOverflow(new FakeDbException(sqlState: "22001")).Should().BeFalse();
        RelationalFaults.IsNumericOverflow(new Microsoft.Data.Sqlite.SqliteException(19, 787)).Should().BeFalse();
    }

    [Fact]
    public void should_classify_nothing_when_the_chain_carries_no_database_exception()
    {
        var exception = new InvalidOperationException("save failed", new IOException("connection reset"));

        RelationalFaults.IsReferenceViolation(exception, out var referenceName).Should().BeFalse();
        RelationalFaults.IsCheckViolation(exception, out var checkName).Should().BeFalse();
        RelationalFaults.IsNotNullViolation(exception).Should().BeFalse();
        RelationalFaults.IsMaxLengthExceeded(exception).Should().BeFalse();
        RelationalFaults.IsNumericOverflow(exception).Should().BeFalse();
        referenceName.Should().BeNull();
        checkName.Should().BeNull();
    }

    [Fact]
    public void should_reject_a_null_exception()
    {
        var checks = new Action[]
        {
            () => RelationalFaults.IsReferenceViolation(null!),
            () => RelationalFaults.IsReferenceViolation(null!, out _),
            () => RelationalFaults.IsCheckViolation(null!),
            () => RelationalFaults.IsCheckViolation(null!, out _),
            () => RelationalFaults.IsNotNullViolation(null!),
            () => RelationalFaults.IsMaxLengthExceeded(null!),
            () => RelationalFaults.IsNumericOverflow(null!),
        };

        foreach (var check in checks)
        {
            check.Should().Throw<ArgumentNullException>();
        }
    }

    private sealed class FakeDbException(string? sqlState = null, int number = 0) : DbException("fake database failure")
    {
        public override string? SqlState { get; } = sqlState;

        public int Number { get; } = number;
    }
}
