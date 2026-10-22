// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.PostgreSql;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared PostgreSQL container fixture pinned to <see cref="TestImages.PostgreSql"/>.
/// Subclass to add per-project database name, credentials, labels, or seed scripts.
/// </summary>
[PublicAPI]
public class HeadlessPostgreSqlFixture()
    : ContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(TestContextMessageSink.Instance)
{
    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithImage(TestImages.PostgreSql);
    }
}
