// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Hosting;

// The describe command writes to the process console, which every test in the assembly shares, so these tests run
// alone instead of beside tests whose hosts also log to the console.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostConsoleCollection
{
    public const string Name = "Host console";
}

/// <summary>What one <c>RunHeadlessAsync</c> call wrote and returned.</summary>
internal sealed record HostConsoleRun(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// Builds a host with the default application builder, its default logging providers included, and runs it with
    /// <paramref name="args" /> while the console streams point at in-memory writers. The streams are swapped before
    /// the host is built because a logging provider may capture the console writer when it is constructed.
    /// </summary>
    public static async Task<HostConsoleRun> RunAsync(
        Action<HostApplicationBuilder> configure,
        string[] args,
        CancellationToken cancellationToken
    )
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            var builder = Host.CreateApplicationBuilder();
            configure(builder);
            var host = builder.Build();

            var exitCode = await host.RunHeadlessAsync(args, cancellationToken);

            return new HostConsoleRun(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    public static Task<HostConsoleRun> DescribeAsync(
        Action<HostApplicationBuilder> configure,
        CancellationToken cancellationToken
    )
    {
        return RunAsync(configure, ["describe"], cancellationToken);
    }

    public JsonDocument ParseStandardOutput() => JsonDocument.Parse(StandardOutput);
}

/// <summary>A section that writes a fixed section and records whether it ran.</summary>
internal sealed class StubSection(string sectionName, Action<Utf8JsonWriter>? write = null) : IHostManifestSection
{
    public string Name => sectionName;

    public int Calls { get; private set; }

    public void Write(Utf8JsonWriter writer)
    {
        Calls++;

        if (write is not null)
        {
            write(writer);

            return;
        }

        writer.WriteStartObject();
        writer.WriteString("name", sectionName);
        writer.WriteEndObject();
    }
}

internal static class HostConsoleServiceCollectionExtensions
{
    public static IServiceCollection AddStubSection(this IServiceCollection services, StubSection section)
    {
        // Not TryAddEnumerable: it collapses instances of one implementation type, and these tests register several.
        return services.AddSingleton<IHostManifestSection>(section);
    }
}
