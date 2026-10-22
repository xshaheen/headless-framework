// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting;

/// <summary>Runs a Headless host from its entry point, or answers a command about it instead.</summary>
[PublicAPI]
public static class HeadlessHostRunExtensions
{
    private const string _DescribeCommand = "describe";
    private const string _ResourcesCommand = ResourcesCommand.Name;
    private const string _SeedCommand = SeedCommand.Name;
    private const string _DeployCommand = "deploy";
    private const string _CheckCommand = "check";

    extension(IHost host)
    {
        /// <summary>
        /// Runs the host until shutdown, or answers a command about it without starting it: when the first argument is
        /// <c>describe</c>, prints the host manifest as JSON to standard output; when it is <c>check</c>, runs the
        /// host's health checks once and prints one line per check; when it is <c>resources</c>, provisions, verifies,
        /// purges, deletes, or lists the host's provisioned resources and prints one line per resource; when it is
        /// <c>seed</c>, runs, verifies, or lists the host's seeders and prints one line per seeder; when it is
        /// <c>deploy</c>, runs <c>resources provision</c> and then <c>seed</c>.
        /// </summary>
        /// <param name="args">
        /// The process arguments. Any first argument other than <c>describe</c>, <c>check</c>, <c>resources</c>,
        /// <c>seed</c>, or <c>deploy</c> runs the host.
        /// </param>
        /// <param name="cancellationToken">Stops the host, or abandons the command.</param>
        /// <returns>
        /// The process exit code: 0 on success; 1 when the manifest fails, when startup validation fails, when a
        /// health check reports <see cref="HealthStatus.Unhealthy" />, when a resource or a seeder fails or times out,
        /// or when a <see cref="BackgroundService" /> faulted while the host ran; 2 when the
        /// <c>resources</c>, <c>seed</c>, or <c>deploy</c> arguments are invalid or match nothing.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Call it from <c>Program.cs</c> in place of <c>RunAsync</c>: <c>return await app.RunHeadlessAsync(args);</c>.
        /// Only the host's own entry point sees its real configuration sources, so every command sees the configuration
        /// the host would run with.
        /// </para>
        /// <para>
        /// Every command first runs the startup validation a real start runs: options marked <c>ValidateOnStart</c>,
        /// then every <see cref="IHeadlessStartupValidator" />. None starts a hosted service. Standard output carries
        /// only the command's result; console logging written meanwhile goes to standard error. That redirect needs a
        /// host built with <c>HostApplicationBuilder</c> or <c>WebApplicationBuilder</c>: on a host built with
        /// <c>Host.CreateDefaultBuilder</c>, the Microsoft console logger keeps the standard output it captured at
        /// build time, so its log lines can still reach standard output.
        /// </para>
        /// <para>
        /// <c>resources</c> takes a required subcommand (<c>provision</c>, <c>verify</c>, <c>purge</c>, <c>delete</c>,
        /// <c>status</c>, or <c>list</c>), <c>--kind</c> and <c>--name</c> filters, an optional <c>--timeout</c> in
        /// seconds for the whole run (none by default, so each resource's own timeouts govern), and <c>--force</c>,
        /// which <c>purge</c> and <c>delete</c> need outside the destructive environments. See
        /// <see cref="IResourceProvisioner" />.
        /// </para>
        /// <para>
        /// <c>seed</c> runs every seeder in dependency order, <c>verify</c>s that the stores hold what the code declares,
        /// or <c>list</c>s them, with <c>--name</c> filters and a <c>--timeout</c> in seconds (default 300); a failed
        /// seeder stops the run. See <see cref="ISeeder" />.
        /// </para>
        /// <para>
        /// <c>deploy</c> takes no arguments: it provisions every resource, then runs every seeder, and stops at the first
        /// step that fails, so a deployment needs one step instead of two.
        /// </para>
        /// <para>
        /// <c>check</c> skips every check tagged <see cref="HeadlessHealthCheckTags.HostState" />, because those report
        /// the state of a running host and a host that was never started always fails them. A
        /// <see cref="HealthStatus.Degraded" /> check does not fail the command.
        /// </para>
        /// <para>Like <c>RunAsync</c>, it disposes the host in every mode.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="args" /> is <see langword="null" />.</exception>
        public async Task<int> RunHeadlessAsync(string[] args, CancellationToken cancellationToken = default)
        {
            Argument.IsNotNull(host);
            Argument.IsNotNull(args);

            if (args.Length > 0 && string.Equals(args[0], _DescribeCommand, StringComparison.Ordinal))
            {
                return await _RunCommandAsync(host, _DescribeAsync, cancellationToken).ConfigureAwait(false);
            }

            if (args.Length > 0 && string.Equals(args[0], _SeedCommand, StringComparison.Ordinal))
            {
                return await _RunCommandAsync(
                        host,
                        (services, stdout, stderr, token) =>
                            SeedCommand.RunAsync(args[1..], services, stdout, stderr, _ValidateStartupAsync, token),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (args.Length > 0 && string.Equals(args[0], _ResourcesCommand, StringComparison.Ordinal))
            {
                return await _RunCommandAsync(
                        host,
                        (services, stdout, stderr, token) =>
                            ResourcesCommand.RunAsync(
                                args[1..],
                                services,
                                stdout,
                                stderr,
                                _ValidateStartupAsync,
                                token
                            ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (args.Length > 0 && string.Equals(args[0], _DeployCommand, StringComparison.Ordinal))
            {
                return await _RunCommandAsync(
                        host,
                        (services, stdout, stderr, token) => _DeployAsync(args[1..], services, stdout, stderr, token),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (args.Length > 0 && string.Equals(args[0], _CheckCommand, StringComparison.Ordinal))
            {
                return await _RunCommandAsync(host, _CheckAsync, cancellationToken).ConfigureAwait(false);
            }

            // Resolved before the run because RunAsync disposes the host. A background service's fault stops the host
            // (or, when the host ignores such faults, leaves a dead component) but lets the process end normally, so
            // without this a crash reads as a clean exit to an orchestrator.
            var backgroundServices = host.Services.GetServices<IHostedService>().OfType<BackgroundService>().ToList();
            var faulted = new List<BackgroundService>();

            // Counted when the host starts stopping, not after: a service that throws while it shuts down (a disposed
            // client, say) must not turn a clean rollout into a failed exit.
            var stopping = host
                .Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping.Register(() =>
                    faulted.AddRange(backgroundServices.Where(static s => s.ExecuteTask?.IsFaulted == true))
                );

            await using (stopping.ConfigureAwait(false))
            {
                await host.RunAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var service in faulted)
            {
                await Console
                    .Error.WriteLineAsync(
                        $"Host stopped: {service.GetType().Name} failed: "
                            + service.ExecuteTask!.Exception!.GetBaseException().Message
                    )
                    .ConfigureAwait(false);
            }

            return faulted.Count == 0 ? 0 : 1;
        }
    }

    // Runs one command with standard output reserved for its result, then disposes the host.
    private static async Task<int> _RunCommandAsync(
        IHost host,
        Func<IServiceProvider, TextWriter, TextWriter, CancellationToken, Task<int>> command,
        CancellationToken cancellationToken
    )
    {
        var stdout = Console.Out;
        var stderr = Console.Error;

        // Console sinks that look the writer up on each write, such as Serilog's, follow this swap. The Microsoft
        // console logger captured stdout when the host was built, so it is moved by its error threshold instead.
        Console.SetOut(stderr);

        try
        {
            if (!_RouteConsoleLoggerToStandardError(host.Services))
            {
                // Said before anything is printed, so an operator piping the output to a parser learns why it may break.
                await stderr
                    .WriteLineAsync(
                        "Warning: this host's console logger cannot be moved off standard output (a Host.CreateDefaultBuilder "
                            + "host), so log lines may precede the command's output. Build the host with "
                            + "Host.CreateApplicationBuilder or WebApplication.CreateBuilder, or turn console logging off "
                            + "for the command."
                    )
                    .ConfigureAwait(false);
            }

            return await command(host.Services, stdout, stderr, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Disposing flushes queued log lines while console output still points at stderr.
            await _DisposeAsync(host).ConfigureAwait(false);
            Console.SetOut(stdout);
        }
    }

    private static async Task<int> _DeployAsync(
        IReadOnlyList<string> args,
        IServiceProvider services,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken
    )
    {
        const string usage = "Usage: deploy";

        if (args.Count > 0)
        {
            var help = args.Count == 1 && args[0] is "-h" or "--help";
            await (help ? stdout : stderr).WriteLineAsync(usage).ConfigureAwait(false);

            return help ? HostCommandLine.ExitSuccess : HostCommandLine.ExitUsage;
        }

        var provisioned = await ResourcesCommand
            .RunAsync(["provision"], services, stdout, stderr, _ValidateStartupAsync, cancellationToken)
            .ConfigureAwait(false);

        if (provisioned != HostCommandLine.ExitSuccess)
        {
            return provisioned;
        }

        // Validated once already, by the provisioning step.
        return await SeedCommand
            .RunAsync([], services, stdout, stderr, static (_, _) => Task.CompletedTask, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> _DescribeAsync(
        IServiceProvider services,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken
    )
    {
        string manifest;

        try
        {
            await _ValidateStartupAsync(services, cancellationToken).ConfigureAwait(false);
#pragma warning disable RCS1261 // Fix makes the code worse: a MemoryStream releases nothing asynchronously, and await using would bring back the ConfigureAwait ceremony.
            using var buffer = new MemoryStream();
#pragma warning restore RCS1261
            await HostManifest.WriteAsync(services, buffer, cancellationToken).ConfigureAwait(false);
            manifest = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
#pragma warning disable CA1031, EPC12 // False positive: the command's process boundary reports any failure as its message and an exit code, by design.
        catch (Exception exception)
        {
            // The message only: validators write it for the operator, and a stack trace adds noise to a CLI error.
            await stderr.WriteLineAsync($"Host manifest failed: {exception.Message}").ConfigureAwait(false);
            await stderr.FlushAsync(cancellationToken).ConfigureAwait(false);

            return 1;
        }
#pragma warning restore CA1031, EPC12

        await stdout.WriteLineAsync(manifest).ConfigureAwait(false);
        await stdout.FlushAsync(cancellationToken).ConfigureAwait(false);

        return 0;
    }

    private static async Task<int> _CheckAsync(
        IServiceProvider services,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken
    )
    {
        HealthReport? report;
        IReadOnlyList<string> skipped;

        try
        {
            await _ValidateStartupAsync(services, cancellationToken).ConfigureAwait(false);

            var registrations = services.GetService<IOptions<HealthCheckServiceOptions>>()?.Value.Registrations ?? [];
            skipped =
            [
                .. registrations
                    .Where(static x => x.Tags.Contains(HeadlessHealthCheckTags.HostState))
                    .Select(static x => x.Name)
                    .Order(StringComparer.Ordinal),
            ];

            // Absent when nothing called AddHealthChecks, so there is nothing to run.
            var healthChecks = services.GetService<HealthCheckService>();
            report = healthChecks is null
                ? null
                : await healthChecks
                    .CheckHealthAsync(
                        static x => !x.Tags.Contains(HeadlessHealthCheckTags.HostState),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
        }
#pragma warning disable CA1031, EPC12 // False positive: the command's process boundary reports any failure as its message and an exit code, by design.
        catch (Exception exception)
        {
            await stderr.WriteLineAsync($"Host check failed: {exception.Message}").ConfigureAwait(false);
            await stderr.FlushAsync(cancellationToken).ConfigureAwait(false);

            return 1;
        }
#pragma warning restore CA1031, EPC12

        var lines = new List<HostCheckLine>();

        if (report is not null)
        {
            foreach (var (name, entry) in report.Entries.OrderBy(static x => x.Key, StringComparer.Ordinal))
            {
                lines.Add(new(name, _Status(entry.Status), _Duration(entry.Duration), _Description(entry)));
            }
        }

        foreach (var name in skipped)
        {
            lines.Add(new(name, "Skipped", "-", "Reports the state of a running host; not run before start."));
        }

        if (lines.Count == 0)
        {
            await stderr.WriteLineAsync("No health checks are registered.").ConfigureAwait(false);
        }

        await stdout.WriteAsync(HostCheckLine.Format(lines)).ConfigureAwait(false);
        await stdout.FlushAsync(cancellationToken).ConfigureAwait(false);

        return report?.Entries.Values.Any(static x => x.Status == HealthStatus.Unhealthy) == true ? 1 : 0;
    }

    private static string _Status(HealthStatus status) =>
        status switch
        {
            HealthStatus.Unhealthy => "Unhealthy",
            HealthStatus.Degraded => "Degraded",
            HealthStatus.Healthy => "Healthy",
            _ => ((int)status).ToString(CultureInfo.InvariantCulture),
        };

    private static string _Duration(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:0}ms");

    private static string _Description(HealthReportEntry entry)
    {
        // Health endpoints keep the driver's message out of the description because they are often anonymous. This
        // command runs in the operator's own terminal or deploy log, where that message is what explains the failure.
        var description = entry.Description;
        var error = entry.Exception?.GetBaseException().Message;

        if (string.IsNullOrWhiteSpace(error) || string.Equals(error, description, StringComparison.Ordinal))
        {
            return _SingleLine(description ?? "");
        }

        return _SingleLine(string.IsNullOrWhiteSpace(description) ? error : $"{description} {error}");
    }

    // One check per line, so a deploy log or a grep reads each check whole.
    private static string _SingleLine(string text) =>
        text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal)
            ? string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            : text;

    private static async Task _ValidateStartupAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // The same order as a real start: the host validates ValidateOnStart options before any lifecycle service.
        services.GetService<IStartupValidator>()?.Validate();

        // Without the resource check: these commands run before, or instead of, the deployment that creates the
        // resources, so a missing table must not stop 'resources provision' from creating it.
        var runner = new StartupValidationRunner(
            services.GetServices<IHeadlessStartupValidator>().Where(static v => v is not ResourceStartupValidator)
        );
        await runner.StartingAsync(cancellationToken).ConfigureAwait(false);
    }

    // Returns false when a Microsoft console logger keeps writing to standard output.
    private static bool _RouteConsoleLoggerToStandardError(IServiceProvider services)
    {
        // The Microsoft console logger's error threshold is the only switch that moves its lines off the stdout writer
        // it holds, and the logger reloads that threshold from configuration. Hosts from HostApplicationBuilder and
        // WebApplicationBuilder expose a configuration that still accepts sources; the last source added wins, and the
        // host never runs afterwards, so the override reaches nothing but the command run.
        if (services.GetService<IConfiguration>() is IConfigurationBuilder configuration)
        {
            configuration.AddInMemoryCollection([new("Logging:Console:LogToStandardErrorThreshold", "Trace")]);

            return true;
        }

        // Matched by name: Headless.Hosting does not reference the console logger package, and needs only to know it is there.
        return !services
            .GetServices<ILoggerProvider>()
            .Any(static provider =>
                string.Equals(
                    provider.GetType().FullName,
                    "Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider",
                    StringComparison.Ordinal
                )
            );
    }

    private static async Task _DisposeAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            host.Dispose();
        }
    }

    private sealed record HostCheckLine(string Name, string Status, string Duration, string Description)
    {
        // Aligned columns: name, status, duration, description, each line ending the same way on every platform.
        public static string Format(List<HostCheckLine> lines)
        {
            if (lines.Count == 0)
            {
                return "";
            }

            var nameWidth = lines.Max(static x => x.Name.Length);
            var statusWidth = lines.Max(static x => x.Status.Length);
            var durationWidth = lines.Max(static x => x.Duration.Length);
            var builder = new StringBuilder();

            foreach (var line in lines)
            {
                builder
                    .Append(line.Name.PadRight(nameWidth))
                    .Append("  ")
                    .Append(line.Status.PadRight(statusWidth))
                    .Append("  ")
                    .Append(line.Duration.PadLeft(durationWidth));

                if (line.Description.Length > 0)
                {
                    builder.Append("  ").Append(line.Description);
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }
    }
}
