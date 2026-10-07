// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Testcontainers.Redis;

/// <summary>Destructive reset helpers for a Redis test container.</summary>
[PublicAPI]
public static class HeadlessRedisContainerExtensions
{
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
