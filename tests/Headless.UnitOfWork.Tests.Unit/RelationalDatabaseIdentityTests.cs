// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Npgsql;

namespace Tests;

public sealed class RelationalDatabaseIdentityTests : TestBase
{
    [Theory]
    [InlineData("tcp://localhost:5432", "tcp://127.0.0.1:5432")]
    [InlineData("tcp://[::1]:5432", "tcp://localhost:5432")]
    [InlineData("tcp://::1:5432", "tcp://127.0.0.1:5432")]
    [InlineData("/var/run/postgresql/.s.PGSQL.5432", "/var/run/postgresql/.s.PGSQL.5432")]
    [InlineData("localhost,1433", "127.0.0.1,1433")]
    [InlineData("tcp:localhost,1433", "localhost,1433")]
    [InlineData(@".\SQLEXPRESS", @"(local)\SQLEXPRESS")]
    [InlineData("LocalHost", "localhost")]
    [InlineData("tcp://DB.Example.com:5432", "tcp://db.example.com:5432")]
    [InlineData(" db.example.com ", "db.example.com")]
    public void should_match_when_data_sources_name_the_same_host(string configured, string candidate)
    {
        _IsSameDatabase(new FakeConnection(configured, "orders"), new FakeConnection(candidate, "orders"))
            .Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("tcp://localhost:5432", "tcp://localhost:5433")]
    [InlineData("localhost2", "localhost")]
    [InlineData("/var/run/PG/.s.PGSQL.5432", "/var/run/pg/.s.PGSQL.5432")]
    [InlineData(".db", ".")]
    [InlineData("db1.example.com", "db2.example.com")]
    [InlineData(@"localhost\SQLEXPRESS", @"localhost\OTHER")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void should_refuse_when_data_sources_differ_or_are_empty(string? configured, string? candidate)
    {
        _IsSameDatabase(new FakeConnection(configured, "orders"), new FakeConnection(candidate, "orders"))
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("orders", "billing")]
    [InlineData("orders", "Orders")]
    [InlineData("", "")]
    public void should_refuse_when_database_names_differ_or_are_empty(string configured, string candidate)
    {
        _IsSameDatabase(new FakeConnection("localhost", configured), new FakeConnection("localhost", candidate))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void should_refuse_when_the_connections_belong_to_different_providers()
    {
        _IsSameDatabase(new FakeConnection("localhost", "orders"), new OtherFakeConnection("localhost", "orders"))
            .Should()
            .BeFalse();
    }

    // Npgsql reports an empty DataSource for an unopened multi-host connection and the reached host once opened; an
    // unopened single-host connection reports that host in the same format, so it stands in for an opened candidate.
    [Theory]
    [InlineData("Host=pg1,pg2;Port=5432;Database=orders", "Host=pg2;Port=5432;Database=orders")]
    [InlineData("Host=pg1,pg2;Port=5432;Database=orders", "Host=PG1;Port=5432;Database=orders")]
    [InlineData("Server=pg1, pg2;Database=orders", "Host=pg2;Database=orders")]
    [InlineData("Host=pg1:5433,pg2;Port=5432;Database=orders", "Host=pg1;Port=5433;Database=orders")]
    [InlineData("Host=localhost,pg2;Port=5432;Database=orders", "Host=127.0.0.1;Port=5432;Database=orders")]
    [InlineData("Host=pg1,pg2;Port=5432;Database=orders", "Host=pg2,pg1;Port=5432;Database=orders")]
    public void should_match_when_the_candidate_reaches_a_listed_host_of_a_multi_host_source(
        string configured,
        string candidate
    )
    {
        _IsSameNpgsqlDatabase(configured, candidate).Should().BeTrue();
    }

    [Theory]
    [InlineData("Host=pg1,pg2;Port=5432;Database=orders", "Host=pg3;Port=5432;Database=orders")]
    [InlineData("Host=pg1,pg2;Port=5432;Database=orders", "Host=pg1;Port=5433;Database=orders")]
    [InlineData("Host=pg1:5433,pg2;Port=5432;Database=orders", "Host=pg1;Port=5432;Database=orders")]
    [InlineData("Host=pg1,pg2;Port=5432;Database=orders", "Host=pg2;Port=5432;Database=billing")]
    [InlineData("Host=pg1,pg2;Port=5432;Database=orders", "Host=pg1,pg3;Port=5432;Database=orders")]
    [InlineData("Host=pg1;Port=5432;Database=orders", "Host=pg1,pg2;Port=5432;Database=orders")]
    public void should_refuse_when_the_candidate_can_reach_a_host_the_multi_host_source_does_not_list(
        string configured,
        string candidate
    )
    {
        _IsSameNpgsqlDatabase(configured, candidate).Should().BeFalse();
    }

    [Fact]
    public void should_refuse_when_a_connection_reports_no_data_source_and_has_no_provider_factory()
    {
        _IsSameDatabase(
                new FakeConnection("", "orders") { ConnectionString = "Host=pg1,pg2" },
                new FakeConnection("pg1", "orders")
            )
            .Should()
            .BeFalse();
    }

    private static bool _IsSameNpgsqlDatabase(string configured, string candidate)
    {
        using var configuredConnection = new NpgsqlConnection(configured);
        using var candidateConnection = new NpgsqlConnection(candidate);

        return RelationalDatabaseIdentity.IsSameDatabase(configuredConnection, candidateConnection);
    }

    private static bool _IsSameDatabase(FakeConnection configured, FakeConnection candidate)
    {
        using (configured)
        using (candidate)
        {
            return RelationalDatabaseIdentity.IsSameDatabase(configured, candidate);
        }
    }

    [SuppressMessage("Design", "CA1812", Justification = "Instantiated by the tests above.")]
    private class FakeConnection(string? dataSource, string database) : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database { get; } = database;

        public override string DataSource { get; } = dataSource!;

        public override string ServerVersion => "0";

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() { }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class OtherFakeConnection(string dataSource, string database) : FakeConnection(dataSource, database);
}
