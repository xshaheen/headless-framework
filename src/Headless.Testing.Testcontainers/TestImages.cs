// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Single source of truth for container images used across integration tests.
/// Every reference includes a reviewed manifest digest so a mutable registry tag cannot change test inputs.
/// </summary>
[PublicAPI]
public static class TestImages
{
    /// <summary>PostgreSQL image tag.</summary>
    public const string PostgreSql =
        "postgres:18.1-alpine3.23@sha256:aa6eb304ddb6dd26df23d05db4e5cb05af8951cda3e0dc57731b771e0ef4ab29";

    /// <summary>Redis image tag.</summary>
    public const string Redis =
        "redis:7-alpine@sha256:6ab0b6e7381779332f97b8ca76193e45b0756f38d4c0dcda72dbb3c32061ab99";

    /// <summary>NATS image tag.</summary>
    public const string Nats = "nats:2-alpine@sha256:c11af972c99ae542de8925e6a7d9c533aa1eb039660420d2074beed6089b3bf0";

    /// <summary>Confluent Kafka image tag.</summary>
    public const string Kafka = "confluentinc/cp-kafka:7.5.12";

    /// <summary>Apache Pulsar image tag.</summary>
    public const string Pulsar = "apachepulsar/pulsar:3.0.9";

    /// <summary>RabbitMQ image tag.</summary>
    public const string RabbitMq =
        "rabbitmq:3-alpine@sha256:d7af1c87c5f1eda13fcfca06db452bf3aeab6619fc3358b68535c0c02c4e52bc";

    /// <summary>Azurite (Azure Storage emulator) image tag.</summary>
    public const string Azurite =
        "mcr.microsoft.com/azure-storage/azurite:3.35.0@sha256:647c63a91102a9d8e8000aab803436e1fc85fbb285e7ce830a82ee5d6661cf37";

    /// <summary>
    /// LocalStack (AWS emulator) image tag.
    /// Pinned to 4.4.0 — the last fully open-source release. LocalStack relicensed
    /// on 2026-03-23: 2026.03.0 and later require a paid <c>LOCALSTACK_AUTH_TOKEN</c>
    /// and exit immediately (code 55) without one. Do not bump past 4.4.0 without
    /// either acquiring a free Hobby-plan token or swapping to an alternative
    /// emulator (e.g., adobe/S3Mock for S3-only workloads).
    /// </summary>
    public const string LocalStack =
        "localstack/localstack:4.4.0@sha256:b52c16663c70b7234f217cb993a339b46686e30a1a5d9279cb5feeb2202f837c";

    /// <summary>
    /// SQL Server 2022 image tag (used on x86_64 hosts).
    /// Pinned to an immutable CU tag rather than the rolling <c>2022-latest</c> so Docker does not
    /// re-check the registry on every run and so a fresh pull cannot silently change the SQL Server build.
    /// </summary>
    public const string MsSqlServer =
        "mcr.microsoft.com/mssql/server:2022-CU19-ubuntu-22.04@sha256:147ee765ff1db3b86ce6ec05908e51fd0dab2feda5dd85b2721f28c77ca305eb";

    /// <summary>
    /// Azure SQL Edge image tag (used on ARM64 hosts; SQL Server doesn't ship ARM images).
    /// Pinned to <c>1.0.7</c> (SQL engine 15.0.2000.x) rather than the rolling <c>latest</c>: the
    /// <c>latest</c> tag moved on to the 2.0.x line, so an unpinned pull would change engine behavior on ARM.
    /// </summary>
    public const string AzureSqlEdge =
        "mcr.microsoft.com/azure-sql-edge:1.0.7@sha256:1dcc88d2d9e555d0addb0636648d0da033206978d7c5c4da044c904a0f06f58b";
}
