// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using DotNet.Testcontainers.Builders;
using Headless.Testing.Testcontainers;

namespace Testcontainers.Redis;

/// <summary>Readiness and destructive reset helpers for a Redis test container.</summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class HeadlessRedisContainerExtensions
{
    extension(RedisBuilder builder)
    {
        /// <summary>
        /// Waits, after the stock in-container <c>redis-cli ping</c>, until this container's Redis answers from the test
        /// host through the mapped port, so the first client connection after the start cannot be refused or reach
        /// another server.
        /// </summary>
        /// <remarks>
        /// The stock check passes before a container runtime such as OrbStack or Docker Desktop forwards the port, and
        /// a TCP-only check passes as soon as the runtime's port proxy accepts, before it can reach Redis. The wait
        /// compares the <c>run_id</c> the port answers with the one read inside the container, and fails the start with
        /// <see cref="InvalidOperationException" /> when another Redis or listener answers on the host port. To restart
        /// on another port instead, start the container through <see cref="PublishedPortCheck.StartAsync" /> with
        /// <see cref="PublishedPortProbes.RedisRunId" />, as <see cref="HeadlessRedisFixture" /> does.
        /// </remarks>
        public RedisBuilder WithHostPingWaitStrategy()
        {
            return builder.WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilCommandIsCompleted("redis-cli", "ping")
                    .AddCustomWaitStrategy(new RedisHostPingWaitUntil())
            );
        }
    }

    extension(RedisContainer container)
    {
        /// <summary>Deletes every key in every database of the container.</summary>
        /// <remarks>
        /// Runs <c>redis-cli FLUSHALL</c> inside the container, so it needs no client connection and no
        /// <c>allowAdmin</c> flag. The flush is synchronous: it returns after the keys are gone.
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>redis-cli</c> exited with a non-zero code.</exception>
        public async Task FlushAllAsync(CancellationToken cancellationToken = default)
        {
            var result = await container.ExecAsync(["redis-cli", "FLUSHALL"], cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"redis-cli FLUSHALL exited with code {result.ExitCode}: {result.Stderr.Trim()}"
                    )
                );
            }
        }
    }
}
