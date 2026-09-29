// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql.PostgreSql;
using Headless.Testing.Tests;

namespace Tests.PostgreSql;

public sealed class PostgreSqlSchemaInitLockTests : TestBase
{
    [Fact]
    public void should_build_the_transaction_scoped_schema_wide_lock_statement()
    {
        // when
        var statement = PostgreSqlSchemaInitLock.AcquireStatement("headless");

        // then: every initializer sharing the schema must derive this exact key, or they stop serializing
        statement.Should().Be("SELECT pg_advisory_xact_lock(hashtextextended('headless_schema_init:headless', 0));");
    }

    [Fact]
    public void should_key_the_lock_on_the_schema_so_different_schemas_do_not_contend()
    {
        // when
        var first = PostgreSqlSchemaInitLock.AcquireStatement("tenant_a");
        var second = PostgreSqlSchemaInitLock.AcquireStatement("tenant_b");

        // then
        first.Should().Contain("'headless_schema_init:tenant_a'");
        second.Should().Contain("'headless_schema_init:tenant_b'");
        first.Should().NotBe(second);
    }
}
