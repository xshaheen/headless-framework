// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Testcontainers.Azurite;
using Testcontainers.Kafka;
using Testcontainers.LocalStack;
using Testcontainers.Nats;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Tests;

/// <summary>
/// Each probe against a real server of the same kind on the port: the container reports the other server's host port
/// until its first restart, as when another test's forwarder to that server holds the port Docker handed out. The check
/// must tell the two apart, restart the container, and accept it on its real port. The fixtures themselves started
/// through their own checks, which proves each probe accepts its own server.
/// </summary>
public sealed class PublishedPortCheckTests : TestBase
{
    private const string _SqlPassword = "YourStrong@Passw0rd";

    [Fact]
    public async Task should_move_a_redis_off_a_port_another_redis_answers()
    {
        await using var own = new HeadlessRedisFixture();
        await using var other = new HeadlessRedisFixture();
        await _StartAsync(own, other);

        await _AssertMovesOffAsync(own.Container, other.Container, RedisBuilder.RedisPort, own.PortCheck);
    }

    [Fact]
    public async Task should_move_a_nats_server_off_a_port_another_nats_server_answers()
    {
        await using var own = new HeadlessNatsFixture();
        await using var other = new HeadlessNatsFixture();
        await _StartAsync(own, other);

        await _AssertMovesOffAsync(own.Container, other.Container, NatsBuilder.NatsClientPort, own.PortCheck);
    }

    [Fact]
    public async Task should_move_a_localstack_off_a_port_another_localstack_answers()
    {
        await using var own = new HeadlessLocalStackFixture();
        await using var other = new HeadlessLocalStackFixture();
        await _StartAsync(own, other);

        await _AssertMovesOffAsync(own.Container, other.Container, LocalStackBuilder.LocalStackPort, own.PortCheck);
    }

    [Fact]
    public async Task should_move_an_azurite_off_a_port_another_azurite_answers()
    {
        await using var own = new HeadlessAzuriteFixture();
        await using var other = new HeadlessAzuriteFixture();
        await _StartAsync(own, other);

        await _AssertMovesOffAsync(own.Container, other.Container, AzuriteBuilder.TablePort, own.PortCheck);
    }

    [Fact]
    public async Task should_move_a_minio_off_a_port_another_minio_answers()
    {
        await using var own = new HeadlessMinioFixture();
        await using var other = new HeadlessMinioFixture();
        await _StartAsync(own, other);

        await _AssertMovesOffAsync(own.Container, other.Container, HeadlessMinioFixture.MinioPort, own.PortCheck);
    }

    // The shared-server fixtures hold an owner session that a restart would cut, so these probes run on bare servers
    // built the way the fixtures build theirs.
    [Fact]
    public async Task should_move_a_rabbitmq_off_a_port_another_rabbitmq_answers()
    {
        await using var own = new RabbitMqBuilder(TestImages.RabbitMq).Build();
        await using var other = new RabbitMqBuilder(TestImages.RabbitMq).Build();
        var check = new PublishedPortCheck(RabbitMqBuilder.RabbitMqPort, PublishedPortProbes.RabbitMqClusterName);
        await Task.WhenAll(check.StartAsync(own, AbortToken), check.StartAsync(other, AbortToken));

        await _AssertMovesOffAsync(own, other, RabbitMqBuilder.RabbitMqPort, check);
    }

    [Fact]
    public async Task should_move_a_postgresql_off_a_port_another_postgresql_answers()
    {
        await using var own = _PostgreSql();
        await using var other = _PostgreSql();
        var check = new PublishedPortCheck(
            PostgreSqlBuilder.PostgreSqlPort,
            PublishedPortProbes.PostgreSqlSystemIdentifier("postgres", "postgres")
        );
        await Task.WhenAll(check.StartAsync(own, AbortToken), check.StartAsync(other, AbortToken));

        await _AssertMovesOffAsync(own, other, PostgreSqlBuilder.PostgreSqlPort, check);
    }

    [Fact]
    public async Task should_move_a_sql_server_off_a_port_another_sql_server_answers()
    {
        await using var own = _SqlServer();
        await using var other = _SqlServer();
        var check = new PublishedPortCheck(1433, PublishedPortProbes.SqlServerMachineName(_SqlPassword));
        await Task.WhenAll(check.StartAsync(own, AbortToken), check.StartAsync(other, AbortToken));

        await _AssertMovesOffAsync(own, other, 1433, check);
    }

    [Fact]
    public async Task should_refuse_a_kafka_port_another_kafka_answers_without_restarting()
    {
        // given - a Kafka advertises the port it started on, so its fixture never restarts it
        await using var own = new KafkaBuilder(TestImages.Kafka).Build();
        await using var other = new KafkaBuilder(TestImages.Kafka).Build();
        await Task.WhenAll(own.StartAsync(AbortToken), other.StartAsync(AbortToken));
        var check = new PublishedPortCheck(KafkaBuilder.KafkaPort, PublishedPortProbes.KafkaClusterId);
        await check.VerifyAsync(own, allowRestart: false, AbortToken);
        var (shadowed, proxy) = ShadowedContainer.Create(
            own,
            KafkaBuilder.KafkaPort,
            other.GetMappedPublicPort(KafkaBuilder.KafkaPort)
        );

        // when
        var act = () => check.VerifyAsync(shadowed, allowRestart: false, AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("another listener answered");
        proxy.Stops.Should().Be(0);
    }

    [Fact]
    public async Task should_start_once_when_no_other_listener_holds_the_port()
    {
        // given - a started Redis whose reported port is its own
        await using var redis = new RedisBuilder(TestImages.Redis).Build();
        await redis.StartAsync(AbortToken);
        var (container, proxy) = ShadowedContainer.Create(
            redis,
            RedisBuilder.RedisPort,
            redis.GetMappedPublicPort(RedisBuilder.RedisPort)
        );

        // when
        await new PublishedPortCheck(RedisBuilder.RedisPort, PublishedPortProbes.RedisRunId).VerifyAsync(
            container,
            cancellationToken: AbortToken
        );

        // then
        proxy.Stops.Should().Be(0);
    }

    private static async Task _StartAsync(IAsyncLifetime own, IAsyncLifetime other)
    {
        await Task.WhenAll(own.InitializeAsync().AsTask(), other.InitializeAsync().AsTask());
    }

    private static async Task _AssertMovesOffAsync(
        IContainer own,
        IContainer other,
        int containerPort,
        PublishedPortCheck check
    )
    {
        // given
        var otherPort = other.GetMappedPublicPort(containerPort);
        var (shadowed, proxy) = ShadowedContainer.Create(own, containerPort, otherPort);

        // when
        await check.VerifyAsync(shadowed, cancellationToken: AbortToken);

        // then - one restart, after which the check accepted the container on its real port
        proxy.Stops.Should().Be(1);
        own.GetMappedPublicPort(containerPort).Should().NotBe(otherPort);
    }

    private static PostgreSqlContainer _PostgreSql()
    {
        return new PostgreSqlBuilder(TestImages.PostgreSql)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithDatabase("postgres")
            .Build();
    }

    private static IContainer _SqlServer()
    {
        return new ContainerBuilder(
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? TestImages.AzureSqlEdge
                : TestImages.MsSqlServer
        )
            .WithPortBinding(1433, assignRandomHostPort: true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", _SqlPassword)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready"))
            .Build();
    }
}
