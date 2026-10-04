// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Applies the messaging schema for tests that build a bare service provider instead of starting a host, so no
/// hosted schema runner ever runs for them.
/// </summary>
[PublicAPI]
public static class MessagingSchemaTestExtensions
{
    /// <summary>
    /// Applies every registered schema contribution through the provider's <see cref="SchemaRunner"/>, and does nothing
    /// when none is registered (the in-memory storage has no schema). Fails as the hosted runner would when a recorded
    /// step's checksum no longer matches.
    /// </summary>
    public static async Task ApplyMessagingSchemaAsync(
        this IServiceProvider provider,
        CancellationToken cancellationToken = default
    )
    {
        var runner = provider.GetService<SchemaRunner>();

        if (runner is not null)
        {
            await runner.ApplyAsync(cancellationToken).ThrowOnChecksumMismatch();
        }
    }

    /// <summary>
    /// Applies <paramref name="contribution"/> with a standalone runner, for tests that construct the storage by hand.
    /// </summary>
    public static async Task ApplyAsync(
        this SchemaContribution contribution,
        CancellationToken cancellationToken = default
    )
    {
        await new SchemaRunner([contribution]).ApplyAsync(cancellationToken).ThrowOnChecksumMismatch();
    }

    private static async Task ThrowOnChecksumMismatch(this Task<SchemaRunnerResult> run)
    {
        var result = await run;
        var edited = result.Mismatches.Where(m => m.Kind == SchemaMismatchKind.Checksum).ToList();

        if (edited.Count > 0)
        {
            throw new InvalidOperationException(
                $"Schema history disagrees with the steps: {string.Join("; ", edited)}"
            );
        }
    }
}
