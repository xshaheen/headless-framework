// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Headless.Blobs;

namespace Tests;

internal static class InMemoryKeyStorage
{
    /// <summary>
    /// A stateful storage substitute: uploads are captured in memory and served back by the listing/read calls, and
    /// deletes remove them, so the real DataProtection key manager can complete a genuine store-then-load cycle
    /// against it. <paramref name="readGate"/> holds every listing until it completes; <paramref name="readsFail"/>
    /// rejects every listing while its value is <see langword="true"/>.
    /// </summary>
    public static IBlobStorage Fake(
        ConcurrentDictionary<string, byte[]>? seed = null,
        Task? readGate = null,
        StrongBox<bool>? readsFail = null
    )
    {
        var stored = seed ?? new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var storage = Substitute.For<IBlobStorage>();
        _ConfigureReads(storage, stored, readGate, readsFail);

        storage
            .UploadAsync(
                Arg.Any<BlobLocation>(),
                Arg.Any<Stream>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                // Honors the token like a real backend, so a stop request surfaces as cancellation.
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();

                return new ValueTask(captureAsync(call.Arg<BlobLocation>(), call.Arg<Stream>()));
            });

        storage
            .DeleteAsync(Arg.Any<BlobLocation>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(stored.TryRemove(call.Arg<BlobLocation>().Path, out _)));

        return storage;

        async Task captureAsync(BlobLocation location, Stream content)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            stored[location.Path] = buffer.ToArray();
        }
    }

    private static void _ConfigureReads(
        IBlobStorage storage,
        ConcurrentDictionary<string, byte[]> stored,
        Task? readGate,
        StrongBox<bool>? readsFail
    )
    {
        storage
            .ListAsync(Arg.Any<BlobQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<BlobPage>(listAsync()));

        storage
            .OpenReadStreamAsync(Arg.Any<BlobLocation>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var path = call.Arg<BlobLocation>().Path;

                var result = stored.TryGetValue(path, out var bytes)
                    ? new BlobDownloadResult(new MemoryStream(bytes), path)
                    : null;

                return ValueTask.FromResult(result);
            });

        async Task<BlobPage> listAsync()
        {
            if (readGate is not null)
            {
                await readGate;
            }

            if (readsFail?.Value == true)
            {
                throw new IOException("Simulated read failure");
            }

            var blobs = stored
                .Select(pair => new BlobInfo
                {
                    BlobKey = pair.Key,
                    Created = DateTimeOffset.UnixEpoch,
                    Modified = DateTimeOffset.UnixEpoch,
                    Size = pair.Value.Length,
                })
                .ToList();

            return new BlobPage(blobs, null);
        }
    }
}
