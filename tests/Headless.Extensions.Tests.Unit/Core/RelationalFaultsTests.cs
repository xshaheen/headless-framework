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
    public void should_recognize_a_unique_violation_wrapped_in_another_exception()
    {
        var wrapper = new InvalidOperationException("save failed", new FakeDbException(sqlState: "23505"));

        RelationalFaults.IsUniqueViolation(wrapper).Should().BeTrue();
    }

    [Fact]
    public void should_not_report_a_unique_violation_when_the_chain_carries_no_database_exception()
    {
        var exception = new InvalidOperationException("save failed", new IOException("connection reset"));

        RelationalFaults.IsUniqueViolation(exception).Should().BeFalse();
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

    [Theory]
    [InlineData("40001", true)]
    [InlineData("40P01", true)]
    [InlineData("23505", true)]
    [InlineData("08006", false)]
    [InlineData("23503", false)]
    [InlineData(null, false)]
    public void should_classify_known_conflicts_separately_from_uncertain_commit_failures(
        string? sqlState,
        bool expected
    )
    {
        var exception = new InvalidOperationException("save failed", new FakeDbException(sqlState: sqlState));

        RelationalFaults.IsConcurrencyConflict(exception).Should().Be(expected);
    }

    [Theory]
    [InlineData(1205, true)]
    [InlineData(3960, true)]
    [InlineData(3961, true)]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(-2, false)]
    [InlineData(547, false)]
    public void should_classify_sql_server_conflicts_in_every_reported_error(int number, bool expected)
    {
        var exception = new Microsoft.Data.SqlClient.SqlException(3621, furtherNumbers: [number]);

        RelationalFaults.IsConcurrencyConflict(exception).Should().Be(expected);
        RelationalFaults.IsConcurrencyConflict(new FakeDbException(number: number)).Should().BeFalse();
    }

    [Fact]
    public void should_not_classify_an_io_failure_as_a_known_conflict()
    {
        RelationalFaults.IsConcurrencyConflict(new IOException("lost commit reply")).Should().BeFalse();
    }

    [Fact]
    public void should_read_the_violated_constraint_name_from_a_postgresql_driver()
    {
        var exception = new InvalidOperationException(
            "save failed",
            new Npgsql.PostgresException("23505", "IX_Members_Email")
        );

        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be("IX_Members_Email");
    }

    [Fact]
    public void should_report_no_name_when_a_sqlstate_driver_exposes_none()
    {
        RelationalFaults
            .IsUniqueViolation(new FakeDbException(sqlState: "23505"), out var constraintName)
            .Should()
            .BeTrue();
        constraintName.Should().BeNull();
    }

    [Fact]
    public void should_report_no_violation_and_no_name_for_another_postgresql_failure()
    {
        RelationalFaults
            .IsUniqueViolation(new Npgsql.PostgresException("23503", "FK_Orders_Members"), out var name)
            .Should()
            .BeFalse();
        name.Should().BeNull();
    }

    [Theory]
    [InlineData(
        2627,
        "Violation of PRIMARY KEY constraint 'PK_Members'. Cannot insert duplicate key in object 'dbo.Members'. The duplicate key value is (1).",
        "PK_Members"
    )]
    [InlineData(
        2627,
        "Violation of UNIQUE KEY constraint 'UQ_Members_Code'. Cannot insert duplicate key in object 'dbo.Members'. The duplicate key value is (a).",
        "UQ_Members_Code"
    )]
    [InlineData(
        2601,
        "Cannot insert duplicate key row in object 'dbo.Members' with unique index 'IX_Members_Email'. The duplicate key value is (a@b.c).",
        "IX_Members_Email"
    )]
    [InlineData(
        2601,
        "Cannot insert duplicate key row in object 'dbo.Members' with unique index 'IX_it's'. The duplicate key value is (x).",
        "IX_it's"
    )]
    // A session whose language localizes the message: the violation is still known, its name is not.
    [InlineData(
        2601,
        "Eine Zeile mit doppeltem Schlüssel kann in das dbo.Members-Objekt mit dem eindeutigen IX_Members_Email-Index nicht eingefügt werden.",
        null
    )]
    public void should_read_the_violated_constraint_name_from_a_sql_server_message(
        int number,
        string message,
        string? expected
    )
    {
        var exception = new Microsoft.Data.SqlClient.SqlException(
            new Microsoft.Data.SqlClient.SqlError(number, message)
        );

        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(expected);
    }

    [Fact]
    public void should_read_the_name_from_the_sql_server_error_that_reported_the_duplicate_key()
    {
        var exception = new Microsoft.Data.SqlClient.SqlException(
            new Microsoft.Data.SqlClient.SqlError(3621, "The statement has been terminated."),
            new Microsoft.Data.SqlClient.SqlError(
                2601,
                "Cannot insert duplicate key row in object 'dbo.Members' with unique index 'IX_Members_Email'. The duplicate key value is (a)."
            )
        );

        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be("IX_Members_Email");
    }

    [Fact]
    public void should_read_the_name_from_a_legacy_sql_server_message()
    {
        var exception = new System.Data.SqlClient.SqlException(
            2627,
            "Violation of PRIMARY KEY constraint 'PK_Members'. Cannot insert duplicate key in object 'dbo.Members'."
        );

        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be("PK_Members");
    }

    [Fact]
    public void should_report_no_name_for_a_sqlite_violation()
    {
        var exception = new Microsoft.Data.Sqlite.SqliteException(19, 2067);

        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().BeNull();
    }

    [Fact]
    public void should_report_no_name_when_the_failure_is_not_a_database_exception()
    {
        RelationalFaults
            .IsUniqueViolation(new IOException("connection reset"), out var constraintName)
            .Should()
            .BeFalse();
        constraintName.Should().BeNull();
    }

    [Fact]
    public void should_reject_a_null_exception()
    {
        var find = () => RelationalFaults.FindDatabaseException(null!);
        var numbers = () => RelationalFaults.GetSqlServerErrorNumbers(null!);
        var unique = () => RelationalFaults.IsUniqueViolation(null!);
        var named = () => RelationalFaults.IsUniqueViolation(null!, out _);
        var conflict = () => RelationalFaults.IsConcurrencyConflict(null!);

        find.Should().Throw<ArgumentNullException>();
        numbers.Should().Throw<ArgumentNullException>();
        unique.Should().Throw<ArgumentNullException>();
        named.Should().Throw<ArgumentNullException>();
        conflict.Should().Throw<ArgumentNullException>();
    }

    private sealed class FakeDbException(string? sqlState = null, int number = 0, Exception? inner = null)
        : DbException("fake database failure", inner)
    {
        public override string? SqlState { get; } = sqlState;

        public int Number { get; } = number;
    }
}
