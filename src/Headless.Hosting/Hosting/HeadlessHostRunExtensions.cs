// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting;

/// <summary>Runs a Headless host from its entry point, or answers a command about it instead.</summary>
[PublicAPI]
public static class HeadlessHostRunExtensions
{
    private const string _DescribeCommand = "describe";

    extension(IHost host)
    {
        /// <summary>
        /// Runs the host until shutdown, or, when the first argument is <c>describe</c>, prints the host description as
        /// JSON to standard output and returns without starting the host.
        /// </summary>
        /// <param name="args">The process arguments. Any first argument other than <c>describe</c> runs the host.</param>
        /// <param name="cancellationToken">Stops the host, or abandons the description.</param>
        /// <returns>The process exit code: 0 on success, 1 when the description fails.</returns>
        /// <remarks>
        /// <para>
        /// Call it from <c>Program.cs</c> in place of <c>RunAsync</c>: <c>return await app.RunHeadlessAsync(args);</c>.
        /// Only the host's own entry point sees its real configuration sources, so the description shows the tuning the
        /// host would run with.
        /// </para>
        /// <para>
        /// <c>describe</c> first runs the startup validation a real start runs: options marked
        /// <c>ValidateOnStart</c>, then every <see cref="IHeadlessStartupValidator" />. It never starts a hosted service.
        /// Standard output carries only the JSON document; console logging written meanwhile goes to standard error.
        /// On any failure, standard output stays empty, the error message goes to standard error, and the exit code is 1.
        /// </para>
        /// <para>Like <c>RunAsync</c>, it disposes the host in both modes.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="args" /> is <see langword="null" />.</exception>
        public async Task<int> RunHeadlessAsync(string[] args, CancellationToken cancellationToken = default)
        {
            Argument.IsNotNull(host);
            Argument.IsNotNull(args);

            if (args.Length > 0 && string.Equals(args[0], _DescribeCommand, StringComparison.Ordinal))
            {
                return await _DescribeAsync(host, cancellationToken).ConfigureAwait(false);
            }

            await host.RunAsync(cancellationToken).ConfigureAwait(false);

            return 0;
        }
    }

    private static async Task<int> _DescribeAsync(IHost host, CancellationToken cancellationToken)
    {
        var stdout = Console.Out;
        var stderr = Console.Error;

        // Console sinks that look the writer up on each write, such as Serilog's, follow this swap. The Microsoft
        // console logger captured stdout when the host was built, so it is moved by its error threshold instead.
        Console.SetOut(stderr);

        try
        {
            _RouteConsoleLoggerToStandardError(host.Services);

            byte[] description;

            try
            {
                await _ValidateStartupAsync(host.Services, cancellationToken).ConfigureAwait(false);
                var buffer = new MemoryStream();
                await using (buffer.ConfigureAwait(false))
                {
                    await HostDescriptionWriter
                        .WriteAsync(host.Services, buffer, cancellationToken)
                        .ConfigureAwait(false);
                    description = buffer.ToArray();
                }
            }
#pragma warning disable CA1031, EPC12 // False positive: the command's process boundary reports any failure as its message and an exit code, by design.
            catch (Exception exception)
            {
                // The message only: validators write it for the operator, and a stack trace adds noise to a CLI error.
                await stderr.WriteLineAsync($"Host description failed: {exception.Message}").ConfigureAwait(false);
                await stderr.FlushAsync(cancellationToken).ConfigureAwait(false);

                return 1;
            }
#pragma warning restore CA1031, EPC12

            await stdout.WriteLineAsync(Encoding.UTF8.GetString(description)).ConfigureAwait(false);
            await stdout.FlushAsync(cancellationToken).ConfigureAwait(false);

            return 0;
        }
        finally
        {
            // Disposing flushes queued log lines while console output still points at stderr.
            await _DisposeAsync(host).ConfigureAwait(false);
            Console.SetOut(stdout);
        }
    }

    private static async Task _ValidateStartupAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // The same order as a real start: the host validates ValidateOnStart options before any lifecycle service.
        services.GetService<IStartupValidator>()?.Validate();

        var runner = new StartupValidationRunner(services.GetServices<IHeadlessStartupValidator>());
        await runner.StartingAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void _RouteConsoleLoggerToStandardError(IServiceProvider services)
    {
        // The Microsoft console logger's error threshold is the only switch that moves its lines off the stdout writer
        // it holds, and the logger reloads that threshold from configuration. Hosts from HostApplicationBuilder and
        // WebApplicationBuilder expose a configuration that still accepts sources; the last source added wins, and the
        // host never runs afterwards, so the override reaches nothing but the description run.
        if (services.GetService<IConfiguration>() is IConfigurationBuilder configuration)
        {
            configuration.AddInMemoryCollection([new("Logging:Console:LogToStandardErrorThreshold", "Trace")]);
        }
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
