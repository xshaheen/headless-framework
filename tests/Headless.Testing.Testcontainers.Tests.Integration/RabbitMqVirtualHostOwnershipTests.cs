// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using RabbitMQ.Client;

namespace Tests;

[CollectionDefinition(DisableParallelization = true)]
public sealed class RabbitMqOwnershipFixture : HeadlessRabbitMqFixture, ICollectionFixture<RabbitMqOwnershipFixture>;

[Collection<RabbitMqOwnershipFixture>]
public sealed class RabbitMqVirtualHostOwnershipTests(RabbitMqOwnershipFixture fixture) : TestBase
{
    [Fact]
    public async Task should_connect_to_a_virtual_host_of_the_fixtures_own()
    {
        var factory = new ConnectionFactory { Uri = new Uri(fixture.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(AbortToken);

        factory.VirtualHost.Should().Be(fixture.VirtualHost);
        fixture.VirtualHost.Should().MatchRegex("^hf_[0-9a-f]{12}$");
    }

    [Fact]
    public async Task should_delete_the_virtual_host_on_dispose()
    {
        var other = await _CreateOwnershipAsync();

        await other.DisposeAsync();

        (await _VirtualHostsAsync()).Should().NotContain(other.VirtualHost).And.Contain(fixture.VirtualHost);
    }

    [Fact]
    public async Task should_sweep_an_unused_virtual_host_once_it_is_past_its_creation_grace()
    {
        // A virtual host with the owner naming and no connection is what a killed run leaves behind; one created just
        // now may belong to an owner that has not connected yet.
        var orphan = TestDatabaseOwner.NewId();
        var fresh = TestDatabaseOwner.NewId();
        await _RabbitMqCtlAsync("add_vhost", orphan, "--description", "0");
        await _RabbitMqCtlAsync(
            "add_vhost",
            fresh,
            "--description",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        );

        var next = await _CreateOwnershipAsync();
        await next.DisposeAsync();

        var virtualHosts = await _VirtualHostsAsync();
        virtualHosts.Should().NotContain(orphan);
        virtualHosts.Should().Contain(fresh, "a virtual host inside its creation grace may still get its owner");
        virtualHosts
            .Should()
            .Contain(fixture.VirtualHost, "a live owner keeps its virtual host through another's sweep");

        await _RabbitMqCtlAsync("delete_vhost", fresh);
    }

    private Task<RabbitMqVirtualHostOwnership> _CreateOwnershipAsync()
    {
        return RabbitMqVirtualHostOwnership.CreateAsync(
            fixture.Container,
            (ushort)fixture.Port,
            fixture.UserName,
            fixture.Password,
            AbortToken
        );
    }

    private async Task<List<string>> _VirtualHostsAsync()
    {
        var output = await _RabbitMqCtlAsync("list_vhosts", "-q", "--no-table-headers", "name");

        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private async Task<string> _RabbitMqCtlAsync(params string[] arguments)
    {
        var result = await fixture.Container.ExecAsync(["rabbitmqctl", .. arguments], AbortToken);
        result.ExitCode.Should().Be(0, result.Stderr);

        return result.Stdout;
    }
}
