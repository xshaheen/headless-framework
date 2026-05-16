// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Single source of truth for container image tags used across integration tests.
/// Pinning to specific tags (rather than <c>:latest</c>) keeps Docker pulls reproducible
/// and avoids repeated registry round-trips that occur with unpinned tags.
/// </summary>
[PublicAPI]
public static class TestImages
{
    /// <summary>PostgreSQL image tag.</summary>
    public const string PostgreSql = "postgres:18.1-alpine3.23";

    /// <summary>Redis image tag.</summary>
    public const string Redis = "redis:7-alpine";

    /// <summary>NATS image tag.</summary>
    public const string Nats = "nats:2-alpine";

    /// <summary>RabbitMQ image tag.</summary>
    public const string RabbitMq = "rabbitmq:3-alpine";

    /// <summary>Azurite (Azure Storage emulator) image tag.</summary>
    public const string Azurite = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    /// <summary>LocalStack (AWS emulator) image tag.</summary>
    public const string LocalStack = "localstack/localstack:2026.04.2";

    /// <summary>SQL Server 2022 image tag (used on x86_64 hosts).</summary>
    public const string MsSqlServer = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>Azure SQL Edge image tag (used on ARM64 hosts; SQL Server doesn't ship ARM images).</summary>
    public const string AzureSqlEdge = "mcr.microsoft.com/azure-sql-edge:latest";
}
