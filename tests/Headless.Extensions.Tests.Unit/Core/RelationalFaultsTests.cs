// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless;
using Headless.Testing.Tests;

namespace Tests.Core;

public sealed class RelationalFaultsTests : TestBase
{
    [Fact]
    public void should_find_the_outermost_database_exception_in_the_chain()
    {
        var driver = new FakeDbException(inner: new FakeDbException(sqlState: "08006"));
        var wrapper = new InvalidOperationException("save failed", driver);

        RelationalFaults.FindDatabaseException(wrapper).Should().BeSameAs(driver);
        RelationalFaults.FindDatabaseException(driver).Should().BeSameAs(driver);
    }

    [Fact]
    public void should_find_no_database_exception_when_the_chain_carries_none()
    {
        var exception = new InvalidOperationException("save failed", new IOException("connection reset"));

        RelationalFaults.FindDatabaseException(exception).Should().BeNull();
    }

    [Fact]
    public void should_enumerate_every_sql_server_error_number_in_reported_order()
    {
        var exception = new Microsoft.Data.SqlClient.SqlException(8152, furtherNumbers: [2627, 3621]);

        RelationalFaults.GetSqlServerErrorNumbers(exception).Should().Equal(8152, 2627, 3621);
    }

    [Fact]
    public void should_fall_back_to_the_single_error_number_when_the_driver_exposes_no_collection()
    {
        // The legacy System.Data.SqlClient fake exposes Number alone, which is the whole read then.
        var exception = new System.Data.SqlClient.SqlException(2601);

        RelationalFaults.GetSqlServerErrorNumbers(exception).Should().Equal(2601);
        RelationalFaults.IsUniqueViolation(exception).Should().BeTrue();
    }

    [Fact]
    public void should_read_no_sql_server_error_number_from_another_drivers_exception()
    {
        // MySQL reports a lock-wait timeout as 1205, SQL Server's deadlock victim: a number is read only from SqlClient.
        RelationalFaults.GetSqlServerErrorNumbers(new FakeDbException(number: 1205)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("23505", true)] // unique_violation
    [InlineData("23503", false)] // foreign_key_violation
    [InlineData("40001", false)] // serialization_failure
    [InlineData(null, false)]
    public void should_recognize_a_postgresql_unique_violation_by_sqlstate(string? sqlState, bool expected)
    {
        RelationalFaults.IsUniqueViolation(new FakeDbException(sqlState: sqlState)).Should().Be(expected);
    }

    [Theory]
    [InlineData(2627, true)] // unique constraint
    [InlineData(2601, true)] // unique index
    [InlineData(547, false)] // foreign key or check constraint
    [InlineData(1205, false)] // deadlock victim
    public void should_recognize_a_sql_server_unique_violation_by_error_number(int number, bool expected)
    {
        var exception = new Microsoft.Data.SqlClient.SqlException(number);

        RelationalFaults.IsUniqueViolation(exception).Should().Be(expected);
    }

    [Fact]
    public void should_recognize_a_sql_server_duplicate_key_that_is_not_the_first_error()
    {
        // SqlClient's Number is Errors[0].Number; a reader of Number alone misses this duplicate key.
        var exception = new Microsoft.Data.SqlClient.SqlException(8152, furtherNumbers: [2627]);

        exception.Number.Should().Be(8152);
        RelationalFaults.IsUniqueViolation(exception).Should().BeTrue();
    }

    [Fact]
    public void should_not_read_a_sql_server_duplicate_key_number_from_another_drivers_exception()
    {
        RelationalFaults.IsUniqueViolation(new FakeDbException(number: 2627)).Should().BeFalse();
    }

    [Theory]
    [InlineData(19, 2067, true)] // SQLITE_CONSTRAINT_UNIQUE
    [InlineData(19, 1555, true)] // SQLITE_CONSTRAINT_PRIMARYKEY
    [InlineData(19, 787, false)] // SQLITE_CONSTRAINT_FOREIGNKEY
    [InlineData(19, 19, false)] // a bare constraint failure (NOT NULL or CHECK reports its own subcode)
    [InlineData(5, 5, false)] // SQLITE_BUSY
    public void should_recognize_a_sqlite_unique_violation_by_extended_result_code(
        int errorCode,
        int extendedErrorCode,
        bool expected
    )
    {
        var exception = new Microsoft.Data.Sqlite.SqliteException(errorCode, extendedErrorCode);

        RelationalFaults.IsUniqueViolation(exception).Should().Be(expected);
    }

    [Fact]
    public void should_reject_a_null_exception()
    {
        var find = () => RelationalFaults.FindDatabaseException(null!);
        var numbers = () => RelationalFaults.GetSqlServerErrorNumbers(null!);
        var unique = () => RelationalFaults.IsUniqueViolation(null!);

        find.Should().Throw<ArgumentNullException>();
        numbers.Should().Throw<ArgumentNullException>();
        unique.Should().Throw<ArgumentNullException>();
    }

    private sealed class FakeDbException(string? sqlState = null, int number = 0, Exception? inner = null)
        : DbException("fake database failure", inner)
    {
        public override string? SqlState { get; } = sqlState;

        public int Number { get; } = number;
    }
}
