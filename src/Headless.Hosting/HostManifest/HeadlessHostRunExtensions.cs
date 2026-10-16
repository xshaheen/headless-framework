// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting;

/// <summary>Runs a Headless host from its entry point, or answers a command about it instead.</summary>
[PublicAPI]
public static class HeadlessHostRunExtensions
{
    private const string _DescribeCommand = "describe";
    private const string _ResourcesCommand = StatefulResourcesCommand.Name;
    private const string _SeedCommand = SeedCommand.Name;

    extension(IHost host)
    {
        /// <summary>
        /// Runs the host until shutdown, or answers a command about it without starting it: when the first argument is
        /// <c>describe</c>, prints the host manifest as JSON to standard output; when it is <c>resources</c>, sets up,
        /// checks, clears, tears down, or lists the host's stateful resources and prints one line per resource; when it
        /// is <c>seed</c>, runs or lists the host's seeders and prints one line per seeder.
        /// </summary>
        /// <param name="args">
        /// The process arguments. Any first argument other than <c>describe</c>, <c>resources</c>, or <c>seed</c> runs
        /// the host.
        /// </param>
        /// <param name="cancellationToken">Stops the host, or abandons the command.</param>
        /// <returns>
        /// The process exit code: 0 on success; 1 when the manifest fails, when startup validation fails, or when a
        /// resource or a seeder fails or times out; 2 when the <c>resources</c> or <c>seed</c> arguments are invalid or
        /// match nothing.
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
        /// <c>resources</c> takes a subcommand (<c>setup</c>, the default, <c>check</c>, <c>clear</c>, <c>teardown</c>,
        /// <c>statistics</c>, or <c>list</c>), <c>--type</c> and <c>--name</c> filters, and a <c>--timeout</c> in
        /// seconds for the whole run (default 60). See <see cref="IStatefulResource" />.
        /// </para>
        /// <para>
        /// <c>seed</c> runs every seeder in priority order, or <c>list</c>s them, with <c>--name</c> filters and a
        /// <c>--timeout</c> in seconds (default 300); a failed seeder stops the run. See <see cref="ISeeder" />.
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
                            StatefulResourcesCommand.RunAsync(
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

            await host.RunAsync(cancellationToken).ConfigureAwait(false);

            return 0;
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

    private static async Task _ValidateStartupAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // The same order as a real start: the host validates ValidateOnStart options before any lifecycle service.
        services.GetService<IStartupValidator>()?.Validate();

        var runner = new StartupValidationRunner(services.GetServices<IHeadlessStartupValidator>());
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
}
