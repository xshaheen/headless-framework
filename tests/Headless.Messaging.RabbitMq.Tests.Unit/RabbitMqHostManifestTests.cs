// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

// The describe command swaps the process console, which every test in the assembly shares.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RabbitMqHostManifestCollection
{
    public const string Name = "RabbitMQ host manifest console";
}

[Collection(RabbitMqHostManifestCollection.Name)]
public sealed class RabbitMqHostManifestTests : TestBase
{
    // Non-routable: a connection attempt hangs until the client's own timeout instead of failing fast, so finishing
    // well inside that timeout proves the command never tried to reach the broker.
    private const string _UnreachableBroker = "10.255.255.1";

    [Fact]
    public async Task should_describe_the_host_without_reaching_the_broker()
    {
        // given
        using var host = new HostBuilder()
            .ConfigureServices(services =>
                services.AddHeadlessMessaging(setup =>
                    setup.UseRabbitMq(options =>
                    {
                        options.HostName = _UnreachableBroker;
                        options.UserName = "manifest";
                        options.Password = "manifest";
                    })
                )
            )
            .Build();

        // when
        var (exitCode, stdout) = await _DescribeAsync(host).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(stdout);
        document.RootElement.GetProperty("sections").TryGetProperty("messaging", out _).Should().BeTrue();
    }

    private static async Task<(int ExitCode, string StandardOutput)> _DescribeAsync(IHost host)
    {
        var originalOut = Console.Out;
        await using var stdout = new StringWriter();
        Console.SetOut(stdout);

        try
        {
            var exitCode = await host.RunHeadlessAsync(["describe"], AbortToken);

            return (exitCode, stdout.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
