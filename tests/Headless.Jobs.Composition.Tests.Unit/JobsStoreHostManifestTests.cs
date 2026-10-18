// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

// The describe command swaps the process console, which every test in the assembly shares.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class JobsStoreHostManifestCollection
{
    public const string Name = "Jobs store host manifest console";
}

[Collection(JobsStoreHostManifestCollection.Name)]
public sealed class JobsStoreHostManifestTests : TestBase
{
    // Non-routable: opening a connection hangs until the driver's own timeout instead of failing fast, so finishing well
    // inside that timeout proves the command never tried to reach the database.
    private const string _UnreachableDatabase =
        "Host=10.255.255.1;Database=jobs;Username=manifest;Password=manifest;Timeout=30";

    [Fact]
    public async Task should_describe_the_host_without_reaching_the_jobs_database()
    {
        // given
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<ApplicationContext>(db => db.UseNpgsql(_UnreachableDatabase));
                services.AddHeadlessJobs(jobs => jobs.UsePostgreSql<ApplicationContext>(_ => { }));
            })
            .Build();

        // when
        var (exitCode, stdout) = await _DescribeAsync(host).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(stdout);
        document.RootElement.GetProperty("sections").TryGetProperty("jobs", out _).Should().BeTrue();
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

    private sealed class ApplicationContext(DbContextOptions<ApplicationContext> options) : DbContext(options);
}
