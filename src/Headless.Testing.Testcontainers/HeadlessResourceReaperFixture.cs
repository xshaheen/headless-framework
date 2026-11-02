// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using DotNet.Testcontainers;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// xUnit v3 assembly fixture that starts Testcontainers' resource reaper (Ryuk) before any test in the assembly runs,
/// and retries the start once when Ryuk does not acknowledge its connection in time.
/// </summary>
/// <remarks>
/// <para>
/// Testcontainers starts Ryuk with the first container of a test process and gives the connection handshake a fixed
/// 60 seconds that no setting changes. Under Docker load (several integration modules starting containers at once)
/// the handshake can miss that window, and the <see cref="ResourceReaperException"/> then fails whichever fixture
/// happened to start first, so every test of the module fails without running. A second attempt starts a new Ryuk
/// container. When it also fails, the fixture throws an error that names the failure as Docker infrastructure.
/// </para>
/// <para>
/// Opt a test assembly in with <c>[assembly: AssemblyFixture(typeof(HeadlessResourceReaperFixture))]</c>. An assembly
/// fixture runs only when tests execute, never during discovery, so listing tests needs no Docker.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HeadlessResourceReaperFixture : IAsyncLifetime
{
    private const int _MaxAttempts = 2;

    /// <summary>Starts Ryuk, retrying once on a <see cref="ResourceReaperException"/>.</summary>
    /// <remarks>
    /// Does nothing when the reaper is disabled (<c>TESTCONTAINERS_RYUK_DISABLED=true</c>) or no Docker endpoint was
    /// found; the first container start then reports the missing Docker endpoint itself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Ryuk did not start in two attempts.</exception>
    public async ValueTask InitializeAsync()
    {
        if (!TestcontainersSettings.ResourceReaperEnabled || !ResourceReaper.IsUnavailable)
        {
            return;
        }

        var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig;

        if (endpoint is null)
        {
            return;
        }

        // The cancellation token is the test run's, so cancelling the run stops the start instead of being retried.
        // The started reaper is discarded: Testcontainers keeps it as the process default that every container joins.
        await StartAsync(
                async cancellationToken =>
                    _ = await ResourceReaper
                        .GetAndStartDefaultAsync(
                            endpoint,
                            ConsoleLogger.Instance,
                            // Every Headless test image is a Linux image, so a Windows-container engine cannot run
                            // these suites at all; skipping the engine probe keeps the startup to one Docker call.
                            isWindowsEngineEnabled: false,
                            cancellationToken
                        )
                        .ConfigureAwait(false),
                static message => TestContext.Current.SendDiagnosticMessage(message),
                TestContext.Current.CancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Does nothing: Ryuk stops on its own once the test process closes its connection.</summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    internal static async Task StartAsync(
        Func<CancellationToken, Task> start,
        Action<string> report,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await start(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (ResourceReaperException e) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= _MaxAttempts)
                {
                    throw new InvalidOperationException(
                        $"Docker infrastructure failure, not a test failure: the Testcontainers resource reaper (Ryuk) did not start in {_MaxAttempts} attempts. Last error: {e.Message} Check Docker's load and health, then re-run the module.",
                        e
                    );
                }

                report(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The Testcontainers resource reaper (Ryuk) did not start on attempt {attempt} of {_MaxAttempts} ({e.Message}); starting a new Ryuk container."
                    )
                );
            }
        }
    }
}
