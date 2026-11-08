// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

/// <summary>A fixture on another image reference: the default image's tag without its digest, so no pull is needed.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlCustomImageFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlCustomImageFixture>
{
    public const string CustomImage = "postgres:18.1-alpine3.23";

    protected override string Image => CustomImage;
}

[Collection<PostgreSqlCustomImageFixture>]
public sealed class PostgreSqlFixtureImageTests(PostgreSqlCustomImageFixture fixture) : TestBase
{
    [Fact]
    public async Task should_run_the_image_a_subclass_names()
    {
        fixture.Container.Image.FullName.Should().StartWith("postgres:18.1-alpine3.23");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand("SELECT current_database();", connection);

        (await command.ExecuteScalarAsync(AbortToken)).Should().Be(fixture.DatabaseName);
    }

    [Fact]
    public void should_share_a_server_of_its_own_image_apart_from_the_default_one()
    {
        if (!SharedTestContainers.IsEnabled)
        {
            return;
        }

        fixture
            .Container.Name.TrimStart('/')
            .Should()
            .Be(
                SharedTestContainers.ContainerName(
                    "postgresql",
                    PostgreSqlCustomImageFixture.CustomImage,
                    "max_connections=1000"
                )
            )
            .And.NotBe(SharedTestContainers.ContainerName("postgresql", TestImages.PostgreSql, "max_connections=1000"));
    }
}
