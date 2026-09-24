// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

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
