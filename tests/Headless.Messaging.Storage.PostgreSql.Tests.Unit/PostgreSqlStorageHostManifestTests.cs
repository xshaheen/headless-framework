// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

// The describe command swaps the process console, which every test in the assembly shares.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlStorageHostManifestCollection
{
    public const string Name = "PostgreSQL storage host manifest console";
}

[Collection(PostgreSqlStorageHostManifestCollection.Name)]
public sealed class PostgreSqlStorageHostManifestTests : TestBase
{
    // Non-routable: opening a connection hangs until the driver's own timeout instead of failing fast, so finishing well
    // inside that timeout proves the command never tried to reach the database.
    private const string _UnreachableDatabase =
        "Host=10.255.255.1;Database=messaging;Username=manifest;Password=manifest;Timeout=30";

    private const string _UnreachableBillingDatabase =
        "Host=10.255.255.1;Database=billing;Username=manifest;Password=manifest;Timeout=30";

    [Fact]
    public async Task should_describe_the_host_without_reaching_the_outbox_database()
    {
        // given
        using var host = new HostBuilder()
            .ConfigureServices(services =>
                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UsePostgreSql(_UnreachableDatabase);
                    setup.AddOutbox().UsePostgreSql(_UnreachableBillingDatabase);
                })
            )
            .Build();

        // when
        var (exitCode, stdout) = await _DescribeAsync(host).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(stdout);
        var outboxes = document.RootElement.GetProperty("sections").GetProperty("messaging").GetProperty("outboxes");
        outboxes
            .EnumerateArray()
            .Select(x => (x.GetProperty("name").GetString(), x.GetProperty("storage").GetString()))
            .Should()
            .Equal(("primary", "PostgreSQL"), ("outbox-1", "PostgreSQL"));
    }

    private static async Task<(int ExitCode, string StandardOutput)> _DescribeAsync(IHost host)
    {
        var originalOut = Console.Out;
        await using var stdout = new StringWriter();
        Console.SetOut(stdout);

        try
        {
            var exitCode = await host.RunHeadlessCommandsAsync(["describe"], AbortToken);

            return (exitCode, stdout.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
